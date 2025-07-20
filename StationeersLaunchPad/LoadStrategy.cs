using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StationeersLaunchPad {
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
    public Stopwatch ElapsedStopwatch = new();
    public List<ModInfo> Mods => LaunchPadConfig.Mods.Where((mod) => mod.Enabled && mod.Source != ModSource.Core).ToList();

    public async UniTask LoadMods()
    {
      Logger.Global.LogDebug($"Assemblies loading...");
      this.ElapsedStopwatch.Restart();
      await this.LoadAssemblies();
      this.ElapsedStopwatch.Stop();
      Logger.Global.LogWarning($"Assembly loading took {this.ElapsedStopwatch.Elapsed.ToString(@"m\:ss\.fff")}");

      Logger.Global.LogDebug($"Assets loading...");
      this.ElapsedStopwatch.Restart();
      await this.LoadAssets();
      this.ElapsedStopwatch.Stop();
      Logger.Global.LogWarning($"Asset loading took {this.ElapsedStopwatch.Elapsed.ToString(@"m\:ss\.fff")}");

      Logger.Global.LogDebug($"Loading entrypoints...");
      this.ElapsedStopwatch.Restart();
      await this.LoadEntryPoints();
      this.ElapsedStopwatch.Stop();
      Logger.Global.LogWarning($"Loading entrypoints took {this.ElapsedStopwatch.Elapsed.ToString(@"m\:ss\.fff")}");
    }

    public void LoadFailed(LoadedMod mod, Exception ex)
    {
      mod.Logger.LogException(ex);
      mod.LoadFailed = true;
      mod.LoadFinished = false;

      LaunchPadConfig.AutoLoad = false;
    }

    public abstract UniTask LoadAssemblies();
    public abstract UniTask LoadAssets();
    public abstract UniTask LoadEntryPoints();
  }

  public class LoadStrategyLinearSerial : LoadStrategy
  {
    public override async UniTask LoadAssemblies()
    {
      foreach (var info in this.Mods)
      {
        var mod = info.Loaded ?? new LoadedMod(info);
        if (mod.LoadedAssemblies || mod.LoadFailed || mod.LoadFinished)
          continue;
        else
          info.Loaded = mod;

        if (!ModLoader.LoadedMods.Contains(mod))
        {
          ModLoader.LoadedMods.Add(mod);
        }

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
      foreach (var info in this.Mods)
      {
        var mod = info.Loaded;
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
      foreach (var info in this.Mods)
      {
        var mod = info.Loaded;
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
      await UniTask.WhenAll(this.Mods.Select(async (info) =>
      {
        var mod = info.Loaded ?? new LoadedMod(info);
        if (mod.LoadedAssemblies || mod.LoadFailed || mod.LoadFinished)
          return;
        else
          info.Loaded = mod;

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
      await UniTask.WhenAll(this.Mods.Select(async (info) =>
      {
        var mod = info.Loaded;
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
      await UniTask.WhenAll(this.Mods.Select(async (info) =>
      {
        var mod = info.Loaded;
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
