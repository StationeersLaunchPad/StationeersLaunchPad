using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using Assets.Scripts;
using Assets.Scripts.Networking.Transports;
using Assets.Scripts.Serialization;
using Assets.Scripts.Util;
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Commands;
using StationeersLaunchPad.Loading;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Repos;
using StationeersLaunchPad.Sources;
using StationeersLaunchPad.UI;
using StationeersLaunchPad.Update;
using Steamworks;

namespace StationeersLaunchPad;

public enum LoadStage
{
  Updating,
  Initializing,
  Searching,
  Configuring,
  Loading,
  Loaded,
  Failed,
  Running,
}

public struct LoadState
{
  public bool AutoLoad;
  public bool SteamDisabled;
}

public class StageWait(double seconds, bool auto)
{
  private readonly Stopwatch stopwatch = Stopwatch.StartNew();
  public readonly double Seconds = seconds;
  public bool Auto = auto;

  public double SecondsRemaining => Seconds - stopwatch.Elapsed.TotalSeconds;
  public bool Done { get => field || (Auto && SecondsRemaining <= 0); private set; } = false;

  public void Skip() => Done = true;
}

public static class LaunchPadConfig
{
  public static SplashBehaviour SplashBehaviour;

  private static ModList modList = ModList.NewEmpty();

  private static LoadStage Stage = LoadStage.Initializing;
  public static bool ModsLoaded => Stage > LoadStage.Configuring;
  public static bool GameRunning => Stage == LoadStage.Running;

  private static bool AutoSort;
  private static bool AutoLoad = true;
  private static bool SteamDisabled;

  private static StageWait CurWait = new(0, false);

  public static void StopAutoLoad()
  {
    AutoLoad = false;
    CurWait.Auto = false;
  }

  public static void ReloadMods()
  {
    if (Stage != LoadStage.Configuring)
      return;
    Logger.Global.LogInfo("Reloading Mod List");
    Stage = LoadStage.Searching;
    CurWait.Skip();
    SLPCommand.RevertStage(CommandStage.Init);
  }

  public static void Draw()
  {
    if (AutoLoad)
    {
      if (AutoLoadWindow.Draw(Stage, CurWait))
        StopAutoLoad();
    }
    else
    {
      var changed = ManualLoadWindow.Draw(Stage, modList, AutoSort);
      HandleChange(changed);
    }

    AlertPopup.Draw();
    FilePicker.Draw();
  }

  public static async void Run()
  {
    // we need to wait a frame so all the RuntimeInitializeOnLoad tasks are complete, otherwise GameManager.IsBatchMode won't be set yet
    await UniTask.Yield();

    var initState = Platform.InitLoadState;
    SteamDisabled = initState.SteamDisabled;
    AutoLoad &= initState.AutoLoad;
    AutoSort = Configs.AutoSortOnStart.Value;

    // The save path on startup was used to load the mod list, so we can't change it at runtime.
    CustomSavePathPatches.SavePath = Configs.SavePathOnStart.Value;
    Settings.CurrentData.SavePath = LaunchPadPaths.SavePath;

    await StageInitializing();
    await StageUpdating();

    var firstLoad = true;
    do
    {
      await StageSearching(firstLoad);
      firstLoad = false;
      await StageConfiguring();
    }
    while (Stage == LoadStage.Searching);
    await StageLoading();
    await StageFinal();

    StartGame();
    await SLPCommand.MoveToStage(CommandStage.GameRunning);
  }

  private static void OnStartupError(Exception ex)
  {
    Logger.Global.LogError("Error occurred during initialization. Mods will not be loaded.");
    Logger.Global.LogException(ex);

    modList = ModList.NewEmpty();
    Stage = LoadStage.Failed;
    StopAutoLoad();
  }

  private static async UniTask StageInitializing()
  {
    Stage = LoadStage.Initializing;
    try
    {
      if (Configs.RunPostUpdateCleanup)
      {
        LaunchPadUpdater.RunPostUpdateCleanup();
        Configs.PostUpdateCleanup.Value = false;
      }
    }
    catch (Exception ex)
    {
      OnStartupError(ex);
      return;
    }

    if (SteamDisabled)
      return;

    try
    {
      var transport = SteamPatches.GetMetaServerTransport();
      transport.InitClient();
      SteamDisabled = !SteamClient.IsValid;
    }
    catch (Exception ex)
    {
      Logger.Global.LogError($"failed to initialize steam: {ex.Message}");
      Logger.Global.LogError("workshop mods will not be loaded");
      Logger.Global.LogError("turn on DisableSteam in LaunchPad Configuration to hide this message");
      StopAutoLoad();
      SteamDisabled = true;
    }

    WorldManager.OnGameDataLoaded += () => SLPRefCheck.RunRefCheck().Forget();
    WorldManager.OnGameDataLoaded += () => OnGameDataLoaded().Forget();

    await SLPCommand.MoveToStage(CommandStage.Init);
  }

  private static async UniTask StageUpdating()
  {
    if (Stage == LoadStage.Failed || !Configs.CheckForUpdate.Value)
      return;

    Stage = LoadStage.Updating;
    try
    {
      Logger.Global.LogInfo("Checking Version");
      var release = await LaunchPadUpdater.GetUpdateRelease();
      if (release == null)
        return;

      if (!Configs.AutoUpdateOnStart.Value && !await LaunchPadUpdater.CheckShouldUpdate(release))
        return;

      if (!await LaunchPadUpdater.UpdateToRelease(release))
        return;

      Logger.Global.LogError($"StationeersLaunchPad updated to {release.TagName}, please restart your game!");
      Configs.PostUpdateCleanup.Value = true;
    }
    catch (Exception ex)
    {
      Logger.Global.LogError("An error occurred during update.");
      Logger.Global.LogException(ex);
      StopAutoLoad();
      return;
    }

    if (!await Platform.ContinueAfterUpdate())
      StopAutoLoad();
  }

  private static async UniTask StageSearching(bool firstLoad)
  {
    if (Stage == LoadStage.Failed) return;
    Stage = LoadStage.Searching;
    try
    {
      Logger.Global.LogInfo("Loading Mod Repos");
      var modRepos = ModRepos.Current = await ModRepos.LoadConfig();
      if (firstLoad && Configs.RepoCheckUpdates.Value)
        await ModRepos.UpdateRepos(modRepos);
      ModRepos.SaveConfig(modRepos);
      if (firstLoad && Configs.RepoModCheckUpdates.Value)
      {
        var updates = ModRepos.GetModUpdateTargets(modRepos);
        // TODO: prompt user when auto-update not enabled
        if (updates.Count > 0)
          await ModRepos.UpdateMods(modRepos, updates);
        ModRepos.SaveConfig(modRepos);
      }

      Logger.Global.LogInfo("Listing Mods");
      modList = ModList.FromDefs(await ModSource.ListAll(new()
      {
        Repos = modRepos,
        SteamDisabled = SteamDisabled,
      }));

      Logger.Global.LogInfo("Loading Mod Config");
      var modConfig = ModConfigUtil.LoadConfig();
      ModRepos.UpdateModPaths(modRepos, modConfig);
      modList.ApplyConfig(modConfig);
      ModConfigUtil.SaveConfig(modList.ToModConfig());

      var depNotice = !modList.CheckDependencies();
      depNotice = modList.DisableDuplicates() || depNotice;
      if (AutoSort)
        depNotice = !modList.SortByDeps() || depNotice;

      if (depNotice && Platform.PauseOnDepNotice)
        StopAutoLoad();

      // Statically scan mod assemblies for game-API mismatches (off the main thread).
      var modSnapshot = modList.AllMods.ToList();
      await UniTask.SwitchToThreadPool();
      ModCompatScanner.ScanAll(modSnapshot);
      await UniTask.SwitchToMainThread();

      PreLoadCheck.Run(modList);

      if (Platform.PauseOnDepNotice && PreLoadCheck.Current.Issues.Any(i => i.Category == "Incompatible"))
      {
        Logger.Global.LogWarning("Incompatible mods detected - pausing auto-load");
        StopAutoLoad();
      }

      Logger.Global.LogInfo("Mod Config Initialized");
    }
    catch (Exception ex)
    {
      OnStartupError(ex);
    }
  }

  private static async UniTask StageConfiguring()
  {
    if (Stage == LoadStage.Failed) return;
    Stage = LoadStage.Configuring;

    CurWait = new(Configs.AutoLoadWaitTime.Value, AutoLoad);

    await SLPCommand.MoveToStage(CommandStage.ConfigLoaded);
    await Platform.Wait(CurWait, CommandStage.ConfigLoaded);
  }

  private static async UniTask StageLoading()
  {
    if (Stage == LoadStage.Failed) return;
    Stage = LoadStage.Loading;

    var stopwatch = Stopwatch.StartNew();

    foreach (var mod in modList.EnabledMods)
      if (mod.Source is not ModSourceType.Core)
        ModLoader.LoadedMods.Add(new(mod));

    var (strategyType, strategyMode) = Configs.LoadStrategy;

    LoadStrategy loadStrategy = (strategyType, strategyMode) switch
    {
      (LoadStrategyType.Linear, LoadStrategyMode.Serial) => new LoadStrategyLinearSerial(),
      (LoadStrategyType.Linear, LoadStrategyMode.Parallel) => new LoadStrategyLinearParallel(),
      _ => throw new Exception($"invalid load strategy ({strategyType}, {strategyMode})")
    };
    if (!await loadStrategy.LoadMods())
      StopAutoLoad();

    stopwatch.Stop();
    Logger.Global.LogWarning($"Took {stopwatch.Elapsed:m\\:ss\\.fff} to load mods.");

    await SLPRefCheck.RunRefCheck();
  }

  private static async UniTask StageFinal()
  {
    if (Stage != LoadStage.Failed)
      Stage = LoadStage.Loaded;

    // Remember which mods failed to load so the next launch can warn about them.
    PreLoadCheck.SaveLoadResults();

    await SLPCommand.MoveToStage(CommandStage.ModsLoaded);

    CurWait = new(Configs.AutoLoadWaitTime.Value, AutoLoad);
    await Platform.Wait(CurWait, CommandStage.ModsLoaded);
    await SLPRefCheck.RunRefCheck();
  }

  public static ModInfo MatchMod(ModData modData) =>
    modData != null ? modList.AllMods.First(mod => mod.DirectoryPath == modData.DirectoryPath) : null;

  private static void HandleChange(ManualLoadWindow.ChangeFlags changed)
  {
    if (changed == ManualLoadWindow.ChangeFlags.None)
      return;
    var sortChanged = changed.HasFlag(ManualLoadWindow.ChangeFlags.AutoSort);
    var modsChanged = changed.HasFlag(ManualLoadWindow.ChangeFlags.Mods);
    if (sortChanged)
      Configs.AutoSortOnStart.Value = AutoSort = !AutoSort;
    if (sortChanged || modsChanged)
    {
      modList.CheckDependencies();
      modList.DisableDuplicates();
      if (AutoSort)
        modList.SortByDeps();
      ModConfigUtil.SaveConfig(modList.ToModConfig());
      PreLoadCheck.Run(modList);
    }
    var next = changed.HasFlag(ManualLoadWindow.ChangeFlags.NextStep);
    if (next)
      CurWait.Skip();
  }

  private static void StartGame()
  {
    Stage = LoadStage.Running;
    var co = (IEnumerator)typeof(SplashBehaviour).GetMethod("AwakeCoroutine", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(SplashBehaviour, []);
    SplashBehaviour.StartCoroutine(co);

    EssentialPatches.GameStarted = true;

    AlertPopup.Close();
    Platform.SetBackgroundEnabled(true);
  }

  private static async UniTask OnGameDataLoaded()
  {
    await UniTask.SwitchToMainThread();
    await SLPCommand.MoveToStage(CommandStage.GameDataLoaded);
  }

  public static string ExportModPackage(string pkgpath = null)
  {
    try
    {
      if (string.IsNullOrEmpty(pkgpath))
        pkgpath = Path.Combine(
          LaunchPadPaths.SavePath,
          $"modpkg_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.zip");
      else
      {
        if (!Path.IsPathRooted(pkgpath))
          pkgpath = Path.Combine(LaunchPadPaths.SavePath, pkgpath);
        if (!pkgpath.ToLower().EndsWith(".zip"))
          pkgpath += ".zip";
      }
      using (var archive = ZipFile.Open(pkgpath, ZipArchiveMode.Create))
      {
        var config = new ModConfig();
        foreach (var mod in modList.EnabledMods)
        {
          if (mod.Source == ModSourceType.Core)
          {
            config.Mods.Add(new CoreModData());
            continue;
          }

          var dirName = $"{mod.Source}_{mod.DirectoryName}";
          var root = mod.DirectoryPath;
          foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
          {
            var entryPath = Path.Combine("mods", dirName, file[(root.Length + 1)..]).Replace('\\', '/');
            archive.CreateEntryFromFile(file, entryPath);
          }
          config.Mods.Add(new LocalModData(dirName, true));
        }

        var configEntry = archive.CreateEntry("modconfig.xml");
        using var stream = configEntry.Open();
        var serializer = new XmlSerializer(typeof(ModConfig));
        serializer.Serialize(stream, config);
      }
      if (!Platform.IsServer)
        ProcessUtil.OpenExplorerSelectFile(pkgpath);
      return $"exported {pkgpath}";
    }
    catch (Exception ex)
    {
      Logger.Global.LogException(ex);
      return ex.ToString();
    }
  }

  // Whether the mod list can still be changed (only before mods start loading).
  public static bool CanImportModList => Stage is LoadStage.Searching or LoadStage.Configuring;

  // Imports an existing StationeersLaunchPad mod package (.zip produced by ExportModPackage /
  // `slp modpkg`): extracts the bundled mods into the local mod folder and applies the
  // package's modconfig.xml, then reloads. Fully compatible with existing .zip packages.
  public static string ImportModPackage(string zippath)
  {
    try
    {
      if (!CanImportModList)
        return "packages can only be imported before mods are loaded";
      if (string.IsNullOrEmpty(zippath) || !File.Exists(zippath))
        return $"file not found: {zippath}";

      var localBase = SteamTransport.WorkshopType.Mod.GetLocalDirInfo().FullName;
      ModConfig config = null;

      using (var archive = ZipFile.OpenRead(zippath))
      {
        foreach (var entry in archive.Entries)
        {
          if (entry.FullName.EndsWith("/"))
            continue;
          if (!entry.FullName.StartsWith("mods/", StringComparison.OrdinalIgnoreCase))
            continue;
          var rel = entry.FullName["mods/".Length..].Replace('/', Path.DirectorySeparatorChar);
          var dest = Path.Combine(localBase, rel);
          Directory.CreateDirectory(Path.GetDirectoryName(dest));
          entry.ExtractToFile(dest, overwrite: true);
        }

        var cfgEntry = archive.GetEntry("modconfig.xml");
        if (cfgEntry != null)
        {
          using var stream = cfgEntry.Open();
          config = (ModConfig)new XmlSerializer(typeof(ModConfig)).Deserialize(stream);
        }
      }

      if (config != null)
      {
        config.CreateCoreMod();
        ModConfigUtil.SaveConfig(config);
      }

      Logger.Global.LogInfo($"Imported mod package {zippath}");
      ReloadMods();
      return $"imported {zippath}";
    }
    catch (Exception ex)
    {
      Logger.Global.LogException(ex);
      return ex.ToString();
    }
  }

  // ── Presets: enabled state + load order only (reuses the ModConfig system) ──────
  public static List<string> ListPresets() => PresetStore.List();

  public static string SavePreset(string name)
  {
    if (string.IsNullOrWhiteSpace(name))
      return "preset name required";
    if (!PresetStore.Save(name, modList.ToModConfig()))
      return "failed to save preset";
    Logger.Global.LogInfo($"Saved preset '{name}'");
    return $"saved preset {name}";
  }

  public static string LoadPreset(string name)
  {
    if (!CanImportModList)
      return "presets can only be loaded before mods are loaded";
    var config = PresetStore.Load(name);
    if (config == null)
      return $"preset '{name}' not found";

    config.CreateCoreMod();
    modList.ApplyConfig(config);
    modList.CheckDependencies();
    modList.DisableDuplicates();
    if (AutoSort)
      modList.SortByDeps();
    ModConfigUtil.SaveConfig(modList.ToModConfig());
    PreLoadCheck.Run(modList);

    Logger.Global.LogInfo($"Loaded preset '{name}'");
    return $"loaded preset {name}";
  }

  public static string DeletePreset(string name)
  {
    PresetStore.Delete(name);
    Logger.Global.LogInfo($"Deleted preset '{name}'");
    return $"deleted preset {name}";
  }
}
