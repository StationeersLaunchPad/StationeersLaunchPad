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
using Assets.Scripts.Serialization;
using Assets.Scripts.Util;
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Commands;
using StationeersLaunchPad.Loading;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Repos;
using StationeersLaunchPad.Sources;
using StationeersLaunchPad.UI;
using StationeersLaunchPad.News;
using StationeersLaunchPad.Update;
using Steamworks;

namespace StationeersLaunchPad;

public enum LoadStage
{
  Updating,
  Initializing,
  News,
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

  private double shift;

  public double SecondsRemaining => Seconds - stopwatch.Elapsed.TotalSeconds + shift;
  public bool Done { get => field || (Auto && SecondsRemaining <= 0); private set; } = false;

  public void Skip() => Done = true;

  // the arrow keys on the splash, never past a full countdown
  public void Shift(double seconds) => shift = Math.Min(shift + seconds, stopwatch.Elapsed.TotalSeconds);
}

public static class LaunchPadConfig
{
  public static SplashBehaviour SplashBehaviour;

  private static ModList modList = ModList.NewEmpty();
  public static ModList ModList => modList;
  private static readonly ProfileManager profileManager = new();
  public static ProfileManager ProfileManager => profileManager;

  private static LoadStage Stage = LoadStage.Initializing;
  public static bool ModsLoaded => Stage > LoadStage.Configuring;
  public static bool Reloading => Stage == LoadStage.Searching;
  public static bool GameRunning => Stage == LoadStage.Running;

  private static bool AutoLoad = true;
  private static bool SkipNextAutoWaits;
  private static bool SteamDisabled;
  private static bool quickProfileOpen;
  private static StageWait takeoffWait;
  private static bool preserveSelectionAfterReload = true;

  private static StageWait CurWait = new(0, false);

  public static void StopAutoLoad()
  {
    AutoLoad = false;
    CurWait.Auto = false;
  }

  public static void SkipAutoWaits()
  {
    if (!AutoLoad)
      return;
    SkipNextAutoWaits = true;
    CurWait.Skip();
  }

  public static void PauseAutoWait()
  {
    if (AutoLoad)
      CurWait.Auto = false;
  }

  private static StageWait NewAutoWait()
  {
    var wait = new StageWait(Configs.AutoLoadWaitTime.Value, AutoLoad);

    if (AutoLoad && SkipNextAutoWaits)
      wait.Skip();

    return wait;
  }

  public static void ReloadMods(bool preserveSelection = true)
  {
    if (Stage != LoadStage.Configuring)
      return;
    preserveSelectionAfterReload = preserveSelection;
    Logger.Global.LogInfo("Reloading Mod List");
    Stage = LoadStage.Searching;
    CurWait.Skip();
    SLPCommand.RevertStage(CommandStage.Init);
  }

  public static void Draw()
  {
    // the box stays locked during the server check and steps aside for the takeoff
    var gateStatus = Networking.Slp2ProfileSync.GateStatus;
    var boxShown = (AutoLoad || gateStatus != null) && Stage != LoadStage.Running;
    if (boxShown)
    {
      // the SLP menu hides the splash, bring it back
      if (gateStatus != null)
        Platform.SetBackgroundEnabled(true);
      var action = LaunchWindow.Draw(Stage, CurWait, profileManager, modList, gateStatus, out var profileChanged);
      if (profileChanged)
      {
        NormalizeModList();
        // a pack with missing mods can't load, so wait for the player
        if (profileManager.GetMissingMods(profileManager.ActiveProfileName, modList).Count > 0)
        {
          quickProfileOpen = true;
          CurWait.Auto = false;
        }
      }

      if (action == LaunchAction.Continue)
      {
        quickProfileOpen = false;
        SkipAutoWaits();
      }
      else if (action is LaunchAction.OpenMenu or LaunchAction.OpenPacks)
      {
        quickProfileOpen = false;
        if (action == LaunchAction.OpenPacks)
        {
          StopAutoLoad();
          ManualLoadWindow.OpenProfilesTab();
        }
        else
        {
          // the menu opens once the rocket has dived off the splash
          CurWait.Auto = false;
          RocketBar.Dive(() =>
          {
            StopAutoLoad();
            ManualLoadWindow.OpenModInfoTab();
          });
        }
      }
    }
    else if (!AutoLoad)
    {
      var changed = ManualLoadWindow.Draw(Stage, modList, profileManager);
      HandleChange(changed);
    }

    if (Stage == LoadStage.Loaded && CurWait is { Auto: true, Done: false } wait && wait != takeoffWait
      && wait.SecondsRemaining <= RocketBar.TakeoffSeconds)
    {
      takeoffWait = wait;
      RocketBar.Launch();
    }
    ImGuiHelper.Draw(RocketBar.DrawFlights);
    SlpDialog.Draw();
    NewsPopup.Draw();
    Networking.Slp2ProfileSync.DrawWarningIfVisible();
    // the box shows it in its header
    if (!boxShown && Stage != LoadStage.Running)
      Networking.Slp2ProfileSync.DrawStatusIfActive();
  }

  public static async void Run()
  {
    // we need to wait a frame so all the RuntimeInitializeOnLoad tasks are complete, otherwise GameManager.IsBatchMode won't be set yet
    await UniTask.Yield();

    var initState = Platform.InitLoadState;
    SkipNextAutoWaits = false;
    SteamDisabled = initState.SteamDisabled;
    AutoLoad &= initState.AutoLoad;

    // The save path on startup was used to load the mod list, so we can't change it at runtime.
    CustomSavePathPatches.SavePath = Configs.SavePathOnStart.Value;
    Settings.CurrentData.SavePath = LaunchPadPaths.SavePath;

    await StageInitializing();
    if (!await StageUpdating())
      return;

    var firstLoad = true;
    do
    {
      await StageSearching(firstLoad);
      await StageConfiguring(firstLoad);
      firstLoad = false;
      // the server check can download missing mods, which reloads the mod list
      if (Stage == LoadStage.Configuring)
        await Networking.Slp2ProfileSync.WaitForGate();
    }
    while (Stage == LoadStage.Searching);
    await StageLoading();
    await StageFinal();

    Networking.Slp2AutoConnect.ArmIfVerified();
    Stage = LoadStage.Running;
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

  // Returns false when startup must stop, such as after a server update requests shutdown.
  private static async UniTask<bool> StageUpdating()
  {
    if (Stage == LoadStage.Failed || !Configs.CheckForUpdate.Value)
      return true;

    Stage = LoadStage.Updating;
    try
    {
      Logger.Global.LogInfo("Checking Version");
      var release = await LaunchPadUpdater.GetUpdateRelease();
      if (release == null)
        return true;

      if (!Configs.AutoUpdateOnStart.Value && !await LaunchPadUpdater.CheckShouldUpdate(release))
        return true;

      if (!await LaunchPadUpdater.UpdateToRelease(release))
        return true;

      Logger.Global.LogError($"StationeersLaunchPad updated to {release.TagName}, please restart your game!");
      Configs.PostUpdateCleanup.Value = true;
    }
    catch (Exception ex)
    {
      Logger.Global.LogError("An error occurred during update.");
      Logger.Global.LogException(ex);
      StopAutoLoad();
      return true;
    }

    if (await Platform.ContinueAfterUpdate())
      return true;

    if (!Platform.IsServer)
    {
      StopAutoLoad();
      return true;
    }

    return false;
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
      var defs = await ModSource.ListAll(new()
      {
        Repos = modRepos,
        SteamDisabled = SteamDisabled,
      });
      modList = ModList.FromDefs(defs);

      Logger.Global.LogInfo("Loading Mod Config");
      var modConfig = ModConfigUtil.LoadConfig();
      ModRepos.UpdateModPaths(modRepos, modConfig);
      modList.ApplyConfig(modConfig);

      // News check now consumes the single mod list built above.
      // This removes the duplicate "repomod load" that used to live in StageNews.
      if (firstLoad)
        await CheckNewsNotices();

      ModConfigUtil.SaveConfig(modList.ToModConfig());

      var depNotice = !modList.CheckDependencies();
      depNotice = (DedupeApplies && modList.DisableDuplicates()) || depNotice;
      depNotice = !modList.SortCanonical() || depNotice;

      if (depNotice && Platform.PauseOnDepNotice)
        StopAutoLoad();

      Logger.Global.LogInfo("Mod Config Initialized");
    }
    catch (Exception ex)
    {
      OnStartupError(ex);
    }
  }

  private static async UniTask CheckNewsNotices()
  {
    if (Platform.IsServer || !Configs.NewsCheckOnStart.Value) return;

    var prevStage = Stage;
    try
    {
      Stage = LoadStage.News;

      var matches = await NewsRunner.GetActiveNotices(modList);
      if (matches.Count > 0)
      {
        Logger.Global.LogInfo($"Displaying {matches.Count} notice(s)");
        await NewsPopup.Run(matches);

        // A repo_mod_install migration may have added a new tracked repo mod.
        // Rebuild the snapshot so the new mod appears enabled before the countdown.
        var repos = ModRepos.Current ?? await ModRepos.LoadConfig();
        ModRepos.Current = repos;

        var defs = await ModSource.ListAll(new()
        {
          Repos = repos,
          SteamDisabled = SteamDisabled,
        });
        modList = ModList.FromDefs(defs);

        var modConfig = ModConfigUtil.LoadConfig();
        ModRepos.UpdateModPaths(repos, modConfig);
        modList.ApplyConfig(modConfig);
      }
      else
      {
        Logger.Global.LogDebug("News: no notices matched current enabled mods / dismissed / already-migrated");
      }
    }
    catch (Exception ex)
    {
      Logger.Global.LogError("Error in news notices stage (startup will continue)");
      Logger.Global.LogException(ex);
    }
    finally
    {
      if (Stage == LoadStage.News)
        Stage = prevStage;
    }
  }

  private static async UniTask StageConfiguring(bool firstLoad)
  {
    if (Stage == LoadStage.Failed) return;
    Stage = LoadStage.Configuring;

    CurWait = NewAutoWait();

    await SLPCommand.MoveToStage(CommandStage.ConfigLoaded);
    PrepareProfileStartup(firstLoad, preserveSelectionAfterReload);
    preserveSelectionAfterReload = true;
    Networking.Slp2ProfileSync.TryStartVerify(profileManager);
    await Platform.Wait(CurWait, CommandStage.ConfigLoaded);
  }

  private static async UniTask StageLoading()
  {
    if (Stage == LoadStage.Failed) return;
    Stage = LoadStage.Loading;
    RocketBar.Launch();

    var stopwatch = Stopwatch.StartNew();

    foreach (var mod in modList.EnabledMods)
      if (mod.Source is not ModSourceType.Core)
        ModLoader.LoadedMods.Add(new(mod));

    if (!await new LoadStrategy().LoadMods())
      StopAutoLoad();

    stopwatch.Stop();
    Logger.Global.LogWarning($"Took {stopwatch.Elapsed:m\\:ss\\.fff} to load mods.");

    await SLPRefCheck.RunRefCheck();
  }

  private static async UniTask StageFinal()
  {
    if (Stage != LoadStage.Failed)
      Stage = LoadStage.Loaded;

    try
    {
      Networking.Slp2Handshake.Initialize();
      Networking.Slp2JoinFailureOffer.Initialize();
      Networking.Slp2ProfileSync.Initialize();
    }
    catch (Exception ex)
    {
      Logger.Global.LogWarning("Failed to initialize SLP2 server handshake");
      Logger.Global.LogException(ex);
    }

    await SLPCommand.MoveToStage(CommandStage.ModsLoaded);

    CurWait = NewAutoWait();
    await Platform.Wait(CurWait, CommandStage.ModsLoaded);
    await SLPRefCheck.RunRefCheck();
  }

  public static ModInfo MatchMod(ModData modData) =>
    modData != null ? modList.AllMods.First(mod => mod.DirectoryPath == modData.DirectoryPath) : null;

  private static void HandleChange(ManualLoadWindow.ChangeFlags changed)
  {
    if (changed == ManualLoadWindow.ChangeFlags.None)
      return;
    var modsChanged = changed.HasFlag(ManualLoadWindow.ChangeFlags.Mods);
    if (modsChanged)
      NormalizeModList();
    var next = changed.HasFlag(ManualLoadWindow.ChangeFlags.NextStep);
    if (next)
    {
      var missingMods = profileManager.ActiveProfile == null
        ? []
        : profileManager.GetMissingMods(profileManager.ActiveProfileName, modList);
      if (missingMods.Count > 0)
      {
        Logger.Global.LogWarning(
          $"Pack '{profileManager.ActiveProfileName}' has {missingMods.Count} missing required mod(s)");
        ProfilePanel.ShowLoadBlocked(profileManager.ActiveProfileName, missingMods.Count);
        ManualLoadWindow.OpenProfilesTab();
      }
      else
        CurWait.Skip();
    }
  }

  private static void PrepareProfileStartup(bool firstLoad, bool preserveSelection)
  {
    // servers don't use packs, they load modconfig.xml as it is
    if (Platform.IsServer)
      return;

    profileManager.EnsureActivePack(modList);
    var profile = profileManager.ActiveProfile;
    if (profile == null)
      return;

    if (!firstLoad && preserveSelection)
    {
      if (profileManager.GetMissingMods(profile.Name, modList).Count > 0)
        ManualLoadWindow.OpenProfilesTab();
      return;
    }
    if (!profileManager.ApplyProfile(profile.Name, modList))
    {
      Logger.Global.LogError($"Could not apply mod pack '{profile.Name}'");
      StopAutoLoad();
      return;
    }
    var depNotice = NormalizeModList();

    var missingMods = profileManager.GetMissingMods(profile.Name, modList);
    if (missingMods.Count > 0)
    {
      Logger.Global.LogWarning(
        $"Pack '{profile.Name}' has {missingMods.Count} missing required mod(s)");
      if (AutoLoad)
      {
        quickProfileOpen = true;
        CurWait.Auto = false;
      }
      else
        ManualLoadWindow.OpenProfilesTab();
      return;
    }

    if (depNotice && Platform.PauseOnDepNotice)
    {
      StopAutoLoad();
      ManualLoadWindow.OpenModInfoTab();
      return;
    }

    if (!AutoLoad)
      return;

    if (quickProfileOpen)
    {
      CurWait.Auto = false;
      return;
    }

  }

  // packs pick which copy of a mod loads themselves
  private static bool DedupeApplies => Platform.IsServer || string.IsNullOrEmpty(Configs.ModProfile.Value);

  // the active pack changed outside the mod list, e.g. a server pack update
  internal static void ReapplyActivePack()
  {
    profileManager.Apply(modList);
    NormalizeModList();
  }

  private static bool NormalizeModList()
  {
    var depNotice = !modList.CheckDependencies();
    depNotice = (DedupeApplies && modList.DisableDuplicates()) || depNotice;
    depNotice = !modList.SortCanonical() || depNotice;
    ModConfigUtil.SaveConfig(modList.ToModConfig());
    return depNotice;
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

  // servers package what they load, clients the active pack
  public static List<ModInfo> ServerPackageMods() =>
    Platform.IsServer ? [.. modList.EnabledMods] : profileManager.ServerPackageMods(modList);

  // returns the path of the written zip
  public static string ExportModPackage(IReadOnlyList<ModInfo> mods, string pkgpath = null)
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
      foreach (var mod in mods)
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
    return pkgpath;
  }
}
