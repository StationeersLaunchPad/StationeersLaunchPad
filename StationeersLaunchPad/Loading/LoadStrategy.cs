using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StationeersLaunchPad.Loading
{
  public enum LoadStrategyType
  {
    // loads in 3 steps:
    // - load all assemblies in order
    // - load asset bundles
    // - find and load entry points
    // each step is done in the order the mods are configured
    // if a mod fails to load, the following steps will be skipped for that mod
    Linear,

    //DependencyFirst,
  }

  public enum LoadStrategyMode
  {
    // load each mod in serial
    Serial,

    // load each mod in parallel
    Parallel,
  }

  public abstract class LoadStrategy
  {
    private bool failed = false;

    // returns true if all mods loaded successfully
    public async UniTask<bool> LoadMods()
    {
      Logger.Global.LogDebug($"Assemblies loading...");
      var stopwatch = Stopwatch.StartNew();
      await this.LoadAssemblies();
      stopwatch.Stop();
      Logger.Global.LogWarning($"Assembly loading took {stopwatch.Elapsed:m\\:ss\\.fff}");

      Logger.Global.LogDebug($"Assets loading...");
      stopwatch.Restart();
      await this.LoadAssets();
      stopwatch.Stop();
      Logger.Global.LogWarning($"Asset loading took {stopwatch.Elapsed:m\\:ss\\.fff}");

      Logger.Global.LogDebug($"Loading entrypoints...");
      stopwatch.Restart();
      await this.LoadEntryPoints();
      stopwatch.Stop();
      Logger.Global.LogWarning($"Loading entrypoints took {stopwatch.Elapsed:m\\:ss\\.fff}");

      return !this.failed;
    }

    public void LoadFailed(LoadedMod mod, Exception ex)
    {
      mod.Logger.LogException(ex);
      mod.LoadFailed = true;
      mod.LoadFinished = false;

      this.failed = true;
    }

    public abstract UniTask LoadAssemblies();
    public abstract UniTask LoadAssets();
    public abstract UniTask LoadEntryPoints();
  }

  public class LoadStrategyLinearSerial : LoadStrategy
  {
    public override async UniTask LoadAssemblies()
    {
      foreach (var mod in ModLoader.LoadedMods)
      {
        if (mod.LoadedAssemblies || mod.LoadFailed || mod.LoadFinished)
          continue;

        try
        {
          await mod.LoadAssembliesSerial();
          mod.LoadedAssemblies = true;
        }
        catch (Exception ex)
        {
          this.LoadFailed(mod, ex);
        }
      }
    }

    public async override UniTask LoadAssets()
    {
      foreach (var mod in ModLoader.LoadedMods)
      {
        if (mod == null || mod.LoadedAssets || mod.LoadFailed || mod.LoadFinished)
          continue;

        try
        {
          await mod.LoadAssetsSerial();
          mod.LoadedAssets = true;
        }
        catch (Exception ex)
        {
          this.LoadFailed(mod, ex);
        }
      }
    }

    public async override UniTask LoadEntryPoints()
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
          this.LoadFailed(mod, ex);
        }
      }
    }
  }

  public class LoadStrategyLinearParallel : LoadStrategy
  {
    public override async UniTask LoadAssemblies()
    {
      await UniTask.WhenAll(ModLoader.LoadedMods.Select(async (mod) =>
      {
        if (mod.LoadedAssemblies || mod.LoadFailed || mod.LoadFinished)
          return;

        if (!ModLoader.LoadedMods.Contains(mod))
          ModLoader.LoadedMods.Add(mod);

        try
        {
          await mod.LoadAssembliesParallel();
          mod.LoadedAssemblies = true;
        }
        catch (Exception ex)
        {
          this.LoadFailed(mod, ex);
        }
      }));
    }

    public async override UniTask LoadAssets()
    {
      await UniTask.WhenAll(ModLoader.LoadedMods.Select(async (mod) =>
      {
        if (mod == null || mod.LoadedAssets || mod.LoadFailed || mod.LoadFinished)
          return;

        try
        {
          await mod.LoadAssetsSerial();
          mod.LoadedAssets = true;
        }
        catch (Exception ex)
        {
          this.LoadFailed(mod, ex);
        }
      }));
    }

    public async override UniTask LoadEntryPoints()
    {
      await UniTask.WhenAll(ModLoader.LoadedMods.Select(async (mod) =>
      {
        if (mod == null || mod.LoadedEntryPoints || mod.LoadFailed || mod.LoadFinished)
          return;

        try
        {
          await mod.FindEntrypoints();
          mod.PrintEntrypoints();
          mod.LoadEntrypoints();
          mod.LoadedEntryPoints = true;
        }
        catch (Exception ex)
        {
          this.LoadFailed(mod, ex);
        }
      }));
    }
  }
}
