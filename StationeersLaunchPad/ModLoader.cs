using BepInEx;
using BepInEx.Bootstrap;
using Cysharp.Threading.Tasks;
using Mono.Cecil;
using StationeersMods.Interface;
using StationeersMods.Shared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace StationeersLaunchPad
{
  public static class ModLoader
  {
    public static readonly List<LoadedMod> LoadedMods = [];
    private static readonly ConcurrentDictionary<Assembly, LoadedMod> AssemblyToMod = new();

    static ModLoader() => TypeLoader.AssemblyResolve += ResolveOnFailure;

    private static AssemblyDefinition ResolveOnFailure(object sender, AssemblyNameReference reference)
    {
      if (!Utility.TryParseAssemblyName(reference.FullName, out var name))
        return null;

      foreach (var mod in LoadedMods)
      {
        foreach (var assembly in mod.Assemblies)
        {
          if (assembly.Info.Name == name.Name)
            return assembly.Info.Definition;
        }
      }

      return null;
    }

    public static void RegisterAssembly(Assembly assembly, LoadedMod mod)
        => AssemblyToMod[assembly] = mod;

    public static bool TryGetExecutingMod(out LoadedMod mod)
        => TryGetStackTraceMod(new StackTrace(3), out mod);

    public static bool TryGetStackTraceMod(StackTrace st, out LoadedMod mod)
    {
      for (var i = 0; i < st.FrameCount; i++)
      {
        var frame = st.GetFrame(i);
        var assembly = frame.GetMethod()?.DeclaringType?.Assembly;
        if (assembly != null && AssemblyToMod.TryGetValue(assembly, out mod))
          return true;
      }
      mod = null;
      return false;
    }

    public static UniTask<LoadedAssembly> LoadAssembly(AssemblyInfo info)
        => UniTask.RunOnThreadPool(() => new LoadedAssembly
        {
          Info = info,
          Assembly = Assembly.LoadFrom(info.Path),
        });

    public static async UniTask WaitFor(AsyncOperation op)
    {
      while (!op.isDone)
        await UniTask.Yield();
    }

    public static async UniTask<AssetBundle> LoadAssetBundle(string path)
    {
      var request = AssetBundle.LoadFromFileAsync(path);
      await WaitFor(request);
      return request.assetBundle;
    }

    public static async UniTask<List<GameObject>> LoadAllBundleAssets(AssetBundle bundle)
    {
      var request = bundle.LoadAllAssetsAsync<GameObject>();
      await WaitFor(request);

      var result = new List<GameObject>(request.allAssets.Length);
      foreach (var asset in request.allAssets)
        result.Add((GameObject) asset);

      return result;
    }

    public static async UniTask<ExportSettings> LoadBundleExportSettings(AssetBundle bundle)
    {
      var request = bundle.LoadAssetAsync<ExportSettings>("ExportSettings");
      await WaitFor(request);
      return (ExportSettings) request.asset;
    }

    public static List<ModEntrypoint> FindExplicitStationeersModsEntrypoints(List<LoadedAssembly> assemblies)
        => FindEntrypoints(assemblies, MakeExplicitStationeersModsEntrypoint);

    private static ModEntrypoint MakeExplicitStationeersModsEntrypoint(LoadedAssembly assembly, TypeDefinition typeDef)
    {
      var attr = typeDef.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == typeof(StationeersMod).FullName);
      return attr == null || !typeDef.IsSubtypeOf(typeof(ModBehaviour))
          ? null
          : new StationeersModsEntrypoint(assembly, typeDef);
    }

    public static List<ModEntrypoint> FindAnyStationeersModsEntrypoints(List<LoadedAssembly> assemblies)
        => FindEntrypoints(assemblies, MakeAnyStationeersModsEntrypoint);

    private static ModEntrypoint MakeAnyStationeersModsEntrypoint(LoadedAssembly assembly, TypeDefinition typeDef)
        => typeDef.IsSubtypeOf(typeof(ModBehaviour))
            ? new StationeersModsEntrypoint(assembly, typeDef)
            : null;

    public static List<ModEntrypoint> FindExportSettingsClassEntrypoints(List<LoadedAssembly> assemblies, List<ExportSettings> exports)
    {
      var startupClasses = new HashSet<string>(
          exports.Select(e => e._startupClass)
                 .Where(s => !string.IsNullOrEmpty(s)));

      return FindEntrypoints(assemblies, (assembly, typeDef)
          => startupClasses.Contains(typeDef.FullName) && typeDef.IsSubtypeOf(typeof(ModBehaviour))
              ? new StationeersModsEntrypoint(assembly, typeDef)
              : null);
    }

    public static List<ModEntrypoint> FindExportSettingsPrefabEntrypoints(List<ExportSettings> exports)
    {
      var seenPrefabs = new HashSet<GameObject>();
      var result = new List<ModEntrypoint>();

      foreach (var exportSettings in exports)
      {
        var entryPrefab = exportSettings._startupPrefab;
        if (entryPrefab != null && seenPrefabs.Add(entryPrefab))
          result.Add(new PrefabEntrypoint(entryPrefab));
      }

      return result;
    }

    public static List<ModEntrypoint> FindBepInExEntrypoints(List<LoadedAssembly> assemblies)
        => FindEntrypoints(assemblies, MakeBepInExEntrypoint);

    private static ModEntrypoint MakeBepInExEntrypoint(LoadedAssembly assembly, TypeDefinition typeDef)
        => typeDef.IsSubtypeOf(typeof(BaseUnityPlugin)) && !typeDef.IsSubtypeOf(typeof(ModBehaviour))
            ? new BepinexEntrypoint(assembly, typeDef)
            : null;

    public const string DEFAULT_METHOD = "OnLoaded";
    public static List<ModEntrypoint> FindDefaultEntrypoints(List<LoadedAssembly> assemblies)
    {
      var result = new List<ModEntrypoint>();

      foreach (var assembly in assemblies)
      {
        var name = assembly.Info.Name;
        var typeDef = assembly.Info.Definition.MainModule.GetType(name);

        var namedEntry = MakeEntrypoint(assembly, MakeDefaultEntrypoint, typeDef);
        if (namedEntry != null)
        {
          result.Add(namedEntry);
          continue;
        }

        result.AddRange(FindEntrypoints(assembly, MakeDefaultEntrypoint));
      }

      return result;
    }

    private static ModEntrypoint MakeDefaultEntrypoint(LoadedAssembly assembly, TypeDefinition typeDef)
    {
      if (!typeDef.IsSubtypeOf(typeof(MonoBehaviour)))
        return null;

      var method = FindDefaultEntrypointMethod(typeDef);
      return method == null ? null : new DefaultEntrypoint(assembly, typeDef);
    }

    private static MethodDefinition FindDefaultEntrypointMethod(TypeDefinition typeDef)
        => FindMethod(typeDef, methodDef =>
            methodDef.Name == DEFAULT_METHOD &&
            methodDef.Parameters.Count == 1 &&
            methodDef.Parameters[0].ParameterType is GenericInstanceType genericType &&
            genericType.GetElementType().FullName == typeof(List<>).FullName &&
            genericType.GenericArguments[0].FullName == typeof(GameObject).FullName);

    private delegate ModEntrypoint TypeToEntrypoint(LoadedAssembly assembly, TypeDefinition typeDef);

    private static List<ModEntrypoint> FindEntrypoints(List<LoadedAssembly> assemblies, TypeToEntrypoint typeToEntrypoint)
    {
      var res = new List<ModEntrypoint>();
      foreach (var assembly in assemblies)
        res.AddRange(FindEntrypoints(assembly, typeToEntrypoint));
      return res;
    }

    private static List<ModEntrypoint> FindEntrypoints(LoadedAssembly assembly, TypeToEntrypoint typeToEntrypoint)
    {
      var result = new List<ModEntrypoint>();
      foreach (var typeDef in assembly.Info.Definition.MainModule.Types)
      {
        var entry = MakeEntrypoint(assembly, typeToEntrypoint, typeDef);
        if (entry != null)
          result.Add(entry);
      }
      return result;
    }

    private static ModEntrypoint MakeEntrypoint(LoadedAssembly assembly, TypeToEntrypoint typeToEntrypoint, TypeDefinition typeDef)
    {
      if (typeDef == null || typeDef.IsAbstract || typeDef.IsInterface)
        return null;

      try
      {
        return typeToEntrypoint(assembly, typeDef);
      }
      catch (AssemblyResolutionException ex)
      {
        Logger.Global.LogDebug($"Skipping type {typeDef.FullName} from {assembly.Info.Name}: failed to resolve {ex.AssemblyReference.FullName}");
        return null;
      }
    }

    private static MethodDefinition FindMethod(TypeDefinition typeDef, Func<MethodDefinition, bool> match)
    {
      while (typeDef != null)
      {
        foreach (var method in typeDef.Methods)
        {
          if (match(method))
            return method;
        }
        typeDef = typeDef.BaseType?.Resolve();
      }
      return null;
    }
  }
}