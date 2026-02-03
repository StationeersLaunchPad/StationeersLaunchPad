using Cysharp.Threading.Tasks;
using StationeersMods.Shared;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace StationeersLaunchPad.Loading
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

    public static bool TryGetAssemblyMod(Assembly assembly, out LoadedMod mod)
    {
      lock (AssembliesLock)
      {
        return AssemblyToMod.TryGetValue(assembly, out mod);
      }
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
  }
}