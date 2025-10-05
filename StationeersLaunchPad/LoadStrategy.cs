using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StationeersLaunchPad
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
    private readonly Stopwatch _stopwatch = new();
    protected List<ModInfo> EnabledMods = [];

    public async UniTask LoadMods()
    {
      // Cache enabled mods only once
      this.EnabledMods = [.. LaunchPadConfig.Mods.Where(m => m.Enabled)];

      await this.MeasurePhase("Assemblies", this.LoadAssemblies);
      await this.MeasurePhase("Assets", this.LoadAssets);
      await this.MeasurePhase("Entrypoints", this.LoadEntryPoints);
    }

    private async UniTask MeasurePhase(string name, Func<UniTask> phase)
    {
      Logger.Global.LogDebug($"{name} loading...");
      this._stopwatch.Restart();
      await phase();
      this._stopwatch.Stop();
      Logger.Global.LogWarning($"{name} loading took {this._stopwatch.Elapsed:m\\:ss\\.fff}");
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
      foreach (var info in this.EnabledMods)
      {
        var mod = info.Loaded ?? new LoadedMod(info, info.Source == ModSource.Core ? Logger.Global : null);
        if (mod.LoadedAssemblies || mod.LoadFailed || mod.LoadFinished)
          continue;

        info.Loaded = mod;

        if (!ModLoader.LoadedMods.Contains(mod))
          ModLoader.LoadedMods.Add(mod);

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

    public override async UniTask LoadAssets()
    {
      foreach (var info in this.EnabledMods)
      {
        var mod = info.Loaded;
        if (info.Source == ModSource.Core || mod == null || mod.LoadedAssets || mod.LoadFailed || mod.LoadFinished)
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

    public override async UniTask LoadEntryPoints()
    {
      foreach (var info in this.EnabledMods)
      {
        var mod = info.Loaded;
        if (info.Source == ModSource.Core || mod == null || mod.LoadedEntryPoints || mod.LoadFailed || mod.LoadFinished)
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
      var tasks = new List<UniTask>();

      foreach (var info in this.EnabledMods)
      {
        tasks.Add(this.LoadAssemblyInternal(info));
      }

      await UniTask.WhenAll(tasks);
    }

    private async UniTask LoadAssemblyInternal(ModInfo info)
    {
      var mod = info.Loaded ?? new LoadedMod(info, info.Source == ModSource.Core ? Logger.Global : null);
      if (mod.LoadedAssemblies || mod.LoadFailed || mod.LoadFinished)
        return;

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
    }

    public override async UniTask LoadAssets()
    {
      var tasks = new List<UniTask>();

      foreach (var info in this.EnabledMods)
      {
        if (info.Source == ModSource.Core)
          continue;

        var mod = info.Loaded;
        if (mod == null || mod.LoadedAssets || mod.LoadFailed || mod.LoadFinished)
          continue;

        tasks.Add(this.LoadAssetsInternal(mod));
      }

      await UniTask.WhenAll(tasks);
    }

    private async UniTask LoadAssetsInternal(LoadedMod mod)
    {
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

    public override async UniTask LoadEntryPoints()
    {
      var tasks = new List<UniTask>();

      foreach (var info in this.EnabledMods)
      {
        var mod = info.Loaded;
        if (info.Source == ModSource.Core || mod == null || mod.LoadedEntryPoints || mod.LoadFailed || mod.LoadFinished)
          continue;

        tasks.Add(this.LoadEntrypointInternal(mod));
      }

      await UniTask.WhenAll(tasks);
    }

    private async UniTask LoadEntrypointInternal(LoadedMod mod)
    {
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
