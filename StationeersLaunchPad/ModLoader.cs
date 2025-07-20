using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using BepInEx;
using StationeersMods.Interface;
using StationeersMods.Shared;
using UnityEngine;
using Mono.Cecil;
using BepInEx.Bootstrap;

namespace StationeersLaunchPad
{
  public static class ModLoader
  {
    public static readonly List<LoadedMod> LoadedMods = new();

    private static object AssembliesLock = new();
    private static readonly Dictionary<Assembly, LoadedMod> AssemblyToMod = new();

    static ModLoader()
    {
      TypeLoader.AssemblyResolve += ResolveOnFailure;
    }

    private static AssemblyDefinition ResolveOnFailure(object sender, AssemblyNameReference reference)
    {
      if (!Utility.TryParseAssemblyName(reference.FullName, out var name))
        return null;

      // resolve against other mod assemblies since they will be loaded
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
    {
      lock (AssembliesLock)
      {
        AssemblyToMod[assembly] = mod;
      }
    }

    public static bool TryGetExecutingMod(out LoadedMod mod)
    {
      return TryGetStackTraceMod(new StackTrace(3), out mod);
    }

    public static bool TryGetStackTraceMod(StackTrace st, out LoadedMod mod)
    {
      lock (AssembliesLock)
      {
        for (var i = 0; i < st.FrameCount; i++)
        {
          var frame = st.GetFrame(i);
          var assembly = frame.GetMethod()?.DeclaringType?.Assembly;
          if (assembly != null && AssemblyToMod.TryGetValue(assembly, out mod))
            return true;
        }
      }
      mod = null;
      return false;
    }

    public static UniTask<LoadedAssembly> LoadAssembly(AssemblyInfo info)
    {
      return UniTask.RunOnThreadPool(() => new LoadedAssembly()
      {
        Info = info,
        Assembly = Assembly.LoadFrom(info.Path),
      });
    }

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
      return request.allAssets.Select(obj => (GameObject) obj).ToList();
    }

    public static async UniTask<ExportSettings> LoadBundleExportSettings(AssetBundle bundle)
    {
      var request = bundle.LoadAssetAsync<ExportSettings>("ExportSettings");
      await WaitFor(request);
      return (ExportSettings) request.asset;
    }

    public static List<ModEntrypoint> FindExplicitStationeersModsEntrypoints(List<LoadedAssembly> assemblies)
    {
      return FindEntrypoints(assemblies, MakeExplicitStationeersModsEntrypoint);
    }
    private static ModEntrypoint MakeExplicitStationeersModsEntrypoint(LoadedAssembly assembly, TypeDefinition typeDef)
    {
      var attr = typeDef.CustomAttributes.FirstOrDefault(attr => attr.AttributeType.FullName == typeof(StationeersMod).FullName);
      if (attr == null)
        return null;

      if (!typeDef.IsSubtypeOf(typeof(ModBehaviour)))
        return null;

      return new StationeersModsEntrypoint(assembly, typeDef);
    }

    public static List<ModEntrypoint> FindAnyStationeersModsEntrypoints(List<LoadedAssembly> assemblies)
    {
      return FindEntrypoints(assemblies, MakeAnyStationeersModsEntrypoint);
    }
    private static ModEntrypoint MakeAnyStationeersModsEntrypoint(LoadedAssembly assembly, TypeDefinition typeDef)
    {
      if (!typeDef.IsSubtypeOf(typeof(ModBehaviour)))
        return null;
      return new StationeersModsEntrypoint(assembly, typeDef);
    }

    public static List<ModEntrypoint> FindExportSettingsClassEntrypoints(List<LoadedAssembly> assemblies, List<ExportSettings> exports)
    {
      var result = new List<ModEntrypoint>();
      var seenTypes = new HashSet<TypeDefinition>();

      var startupClasses = new List<string>();
      foreach (var exportSettings in exports)
      {
        var startupClass = exportSettings._startupClass;
        if (!string.IsNullOrEmpty(startupClass) && !startupClasses.Contains(startupClass))
          startupClasses.Add(startupClass);
      }
      ModEntrypoint MakeExportSettingsClassEntrypoint(LoadedAssembly assembly, TypeDefinition typeDef)
      {
        if (startupClasses.Contains(typeDef.FullName) && typeDef.IsSubtypeOf(typeof(ModBehaviour)))
          return new StationeersModsEntrypoint(assembly, typeDef);
        return null;
      }

      return FindEntrypoints(assemblies, MakeExportSettingsClassEntrypoint);
    }

    public static List<ModEntrypoint> FindExportSettingsPrefabEntrypoints(List<ExportSettings> exports)
    {
      var result = new List<ModEntrypoint>();
      var seenPrefabs = new HashSet<GameObject>();
      foreach (var exportSettings in exports)
      {
        var entryPrefab = exportSettings._startupPrefab;
        if (entryPrefab != null && !seenPrefabs.Contains(entryPrefab))
        {
          seenPrefabs.Add(entryPrefab);
          result.Add(new PrefabEntrypoint(entryPrefab));
        }
      }
      return result;
    }

    public static List<ModEntrypoint> FindBepInExEntrypoints(List<LoadedAssembly> assemblies)
    {
      return FindEntrypoints(assemblies, MakeBepInExEntrypoint);
    }
    private static ModEntrypoint MakeBepInExEntrypoint(LoadedAssembly assembly, TypeDefinition typeDef)
    {
      if (typeDef.IsSubtypeOf(typeof(BaseUnityPlugin)) && !typeDef.IsSubtypeOf(typeof(ModBehaviour)))
        return new BepinexEntrypoint(assembly, typeDef);
      return null;
    }

    public const string DEFAULT_METHOD = "OnLoaded";
    public static List<ModEntrypoint> FindDefaultEntrypoints(List<LoadedAssembly> assemblies)
    {
      var result = new List<ModEntrypoint>();
      foreach (var assembly in assemblies)
      {
        var name = assembly.Info.Name;
        var typeDef = assembly.Info.Definition.MainModule.GetType(name);

        // If we have an entrypoint with the same name as the assembly, use it as the sole entrypoint
        var namedEntry = MakeEntrypoint(assembly, MakeDefaultEntrypoint, typeDef);
        if (namedEntry != null)
        {
          result.Add(namedEntry);
          continue;
        }

        // otherwise take anything that matches the pattern
        result.AddRange(FindEntrypoints(assembly, MakeDefaultEntrypoint));
      }
      return result;
    }
    private static ModEntrypoint MakeDefaultEntrypoint(LoadedAssembly assembly, TypeDefinition typeDef)
    {
      if (!typeDef.IsSubtypeOf(typeof(MonoBehaviour)))
        return null;

      var method = FindDefaultEntrypointMethod(typeDef);
      if (method == null)
        return null;

      return new DefaultEntrypoint(assembly, typeDef);
    }
    private static MethodDefinition FindDefaultEntrypointMethod(TypeDefinition typeDef)
    {
      return typeDef.Methods.FirstOrDefault(methodDef =>
      {
        if (methodDef.Name != DEFAULT_METHOD)
          return false;
        if (methodDef.Parameters.Count != 1)
          return false;
        if (methodDef.Parameters[0].ParameterType is not GenericInstanceType genericType)
          return false;
        if (genericType.GetElementType().FullName != typeof(List<>).FullName)
          return false;
        return genericType.GenericArguments[0].FullName == typeof(GameObject).FullName;
      });
    }

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
        // if we can't resolve the type, we can't use it as an entrypoint
        Logger.Global.LogDebug($"Skipping type {typeDef.FullName} from {assembly.Info.Name}: failed to resolve {ex.AssemblyReference.FullName}");
        return null;
      }
    }
  }
}