using System;
using System.Diagnostics;
using Cysharp.Threading.Tasks;

namespace StationeersLaunchPad.Loading;

// loads in 3 steps:
// - load all assemblies in order
// - load asset bundles
// - find and load entry points
// each step loads one mod at a time in the canonical load order
// if a mod fails to load, the following steps will be skipped for that mod
public class LoadStrategy
{
  private bool failed = false;

  // returns true if all mods loaded successfully
  public async UniTask<bool> LoadMods()
  {
    Logger.Global.LogDebug($"Assemblies loading...");
    var stopwatch = Stopwatch.StartNew();
    await LoadAssemblies();
    stopwatch.Stop();
    Logger.Global.LogWarning($"Assembly loading took {stopwatch.Elapsed:m\\:ss\\.fff}");

    Logger.Global.LogDebug($"Assets loading...");
    stopwatch.Restart();
    await LoadAssets();
    stopwatch.Stop();
    Logger.Global.LogWarning($"Asset loading took {stopwatch.Elapsed:m\\:ss\\.fff}");

    Logger.Global.LogDebug($"Loading entrypoints...");
    stopwatch.Restart();
    await LoadEntryPoints();
    stopwatch.Stop();
    Logger.Global.LogWarning($"Loading entrypoints took {stopwatch.Elapsed:m\\:ss\\.fff}");

    return !failed;
  }

  private void LoadFailed(LoadedMod mod, Exception ex)
  {
    mod.Logger.LogException(ex);
    mod.LoadFailed = true;
    mod.LoadFinished = false;

    failed = true;
  }

  private async UniTask LoadAssemblies()
  {
    foreach (var mod in ModLoader.LoadedMods)
    {
      if (mod.LoadedAssemblies || mod.LoadFailed || mod.LoadFinished)
        continue;

      try
      {
        await mod.LoadAssemblies();
        mod.LoadedAssemblies = true;
      }
      catch (Exception ex)
      {
        LoadFailed(mod, ex);
      }
    }
  }

  private async UniTask LoadAssets()
  {
    foreach (var mod in ModLoader.LoadedMods)
    {
      if (mod == null || mod.LoadedAssets || mod.LoadFailed || mod.LoadFinished)
        continue;

      try
      {
        await mod.LoadAssets();
        mod.LoadedAssets = true;
      }
      catch (Exception ex)
      {
        LoadFailed(mod, ex);
      }
    }
  }

  private async UniTask LoadEntryPoints()
  {
    foreach (var mod in ModLoader.LoadedMods)
    {
      if (mod == null || mod.LoadedEntryPoints || mod.LoadFailed || mod.LoadFinished)
        continue;

      try
      {
        await mod.FindEntrypoints();
        mod.PrintEntrypoints();
        mod.LoadEntrypoints();
        mod.LoadedEntryPoints = true;
      }
      catch (Exception ex)
      {
        LoadFailed(mod, ex);
      }
    }
  }
}
