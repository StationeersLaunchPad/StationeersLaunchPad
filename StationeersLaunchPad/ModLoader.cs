using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using BepInEx;
using StationeersMods.Interface;
using StationeersMods.Shared;
using UnityEngine;
using System.Diagnostics.Contracts;

namespace StationeersLaunchPad
{
  public static class ModLoader
  {
    public static readonly List<LoadedMod> LoadedMods = new();

    private static object AssembliesLock = new();
    private static readonly Dictionary<Assembly, LoadedMod> AssemblyToMod = new();

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

    public static UniTask<Assembly> LoadAssembly(string path)
    {
      return UniTask.RunOnThreadPool(() => Assembly.LoadFrom(path));
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
      return request.allAssets.Select(obj => (GameObject)obj).ToList();
    }

    public static async UniTask<ExportSettings> LoadBundleExportSettings(AssetBundle bundle)
    {
      var request = bundle.LoadAssetAsync<ExportSettings>("ExportSettings");
      await WaitFor(request);
      return (ExportSettings)request.asset;
    }

    public static List<Type> FindExplicitStationeersModsEntrypoints(List<Assembly> assemblies)
    {
      var result = new List<Type>();
      foreach (var assembly in assemblies)
      {
        foreach (var type in assembly.GetTypes())
        {
          var attr = type.GetCustomAttribute<StationeersMod>();
          if (attr != null && typeof(ModBehaviour).IsAssignableFrom(type) && !result.Contains(type))
            result.Add(type);
        }
      }
      return result;
    }

    public static List<Type> FindAnyStationeersModsEntrypoints(List<Assembly> assemblies)
    {
      var result = new List<Type>();
      foreach (var assembly in assemblies)
      {
        foreach (var type in assembly.GetTypes())
        {
          if (typeof(ModBehaviour).IsAssignableFrom(type) && !result.Contains(type))
            result.Add(type);
        }
      }
      return result;
    }

    public static List<Type> FindExportSettingsClassEntrypoints(List<Assembly> assemblies, List<ExportSettings> exports)
    {
      var result = new List<Type>();
      foreach (var exportSettings in exports)
      {
        var startupClass = exportSettings._startupClass;
        if (string.IsNullOrEmpty(startupClass))
          continue;
        foreach (var assembly in assemblies)
        {
          var type = assembly.GetType(startupClass);
          if (type != null && !result.Contains(type))
            result.Add(type);
        }
      }
      return result;
    }

    public static List<GameObject> FindExportSettingsPrefabEntrypoints(List<ExportSettings> exports)
    {
      var result = new List<GameObject>();
      foreach (var exportSettings in exports)
      {
        var entryPrefab = exportSettings._startupPrefab;
        if (entryPrefab != null && !result.Contains(entryPrefab))
          result.Add(entryPrefab);
      }
      return result;
    }

    public static List<Type> FindBepInExEntrypoints(List<Assembly> assemblies)
    {
      var result = new List<Type>();
      foreach (var assembly in assemblies)
      {
        foreach (var type in assembly.GetTypes())
        {
          if (typeof(BaseUnityPlugin).IsAssignableFrom(type) && !typeof(ModBehaviour).IsAssignableFrom(type) && !result.Contains(type))
            result.Add(type);
        }
      }
      return result;
    }

    public const string DEFAULT_METHOD = "OnLoaded";
    public static List<(Type, MethodInfo)> FindDefaultEntrypoints(List<Assembly> assemblies)
    {
      var result = new List<(Type, MethodInfo)>();
      foreach (var assembly in assemblies)
      {
        var failed = true;

        var name = assembly.GetName().Name;
        var type = assembly.GetType(name);
        if (type == null || type.IsAbstract || !typeof(MonoBehaviour).IsAssignableFrom(type))
          failed = true;

        var method = type?.GetMethod(DEFAULT_METHOD, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(List<GameObject>) }, null);
        if (method == null || result.Contains((type, method)))
          failed = true;
        else if (!failed)
          result.Add((type, method));

        if (failed)
        {
          foreach (var defType in assembly.GetTypes())
          {
            if (defType == null || defType.IsAbstract || !typeof(MonoBehaviour).IsAssignableFrom(defType))
              continue;

            var defMethod = defType.GetMethod(DEFAULT_METHOD, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(List<GameObject>) }, null);
            if (defMethod == null || result.Contains((defType, defMethod)))
              continue;
            result.Add((defType, defMethod));
          }
        }
      }
      return result;
    }
  }
}