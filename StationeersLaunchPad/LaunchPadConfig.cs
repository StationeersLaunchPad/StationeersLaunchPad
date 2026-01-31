using Assets.Scripts;
using Assets.Scripts.Serialization;
using Assets.Scripts.Util;
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Loading;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;
using StationeersLaunchPad.UI;
using StationeersLaunchPad.Update;
using Steamworks;
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;

namespace StationeersLaunchPad
{
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

  public class StageWait
  {
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    public readonly double Seconds;
    public readonly bool Auto;
    private bool skipped = false;

    public StageWait(double seconds, bool auto) => (Seconds, Auto) = (seconds, auto);

    public double SecondsRemaining =>
      Math.Clamp(stopwatch.Elapsed.TotalSeconds - Seconds, 0, Seconds);
    public bool Done => skipped || (Auto && SecondsRemaining <= 0);

    public void Skip() => skipped = true;
  }

  public static class LaunchPadConfig
  {
    public static SplashBehaviour SplashBehaviour;

    private static ModList modList = ModList.NewEmpty();

    private static LoadStage Stage = LoadStage.Initializing;

    private static bool AutoSort;
    private static bool AutoLoad = true;
    private static bool SteamDisabled;

    private static StageWait CurWait = new(0, false);

    public static void StopAutoLoad() => AutoLoad = false;

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
      await StageSearching();
      SLPCommand.RunStartup();
      await StageConfiguring();
      await StageLoading();
      await StageFinal();

      StartGame();
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

    private static async UniTask StageSearching()
    {
      if (Stage == LoadStage.Failed) return;
      Stage = LoadStage.Searching;
      try
      {
        Logger.Global.LogInfo("Listing Mods");
        modList = ModList.FromDefs(await ModSource.ListAll(!SteamDisabled));

        Logger.Global.LogInfo("Loading Mod Config");
        modList.ApplyConfig(ModConfigUtil.LoadConfig());
        ModConfigUtil.SaveConfig(modList.ToModConfig());

        var depNotice = !modList.CheckDependencies();
        depNotice = modList.DisableDuplicates(Configs.DisableDuplicates.Value) || depNotice;
        if (AutoSort)
          depNotice = !modList.SortByDeps() || depNotice;

        if (depNotice && Platform.PauseOnDepNotice)
          StopAutoLoad();

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
      await Platform.Wait(CurWait);
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
    }

    private static async UniTask StageFinal()
    {
      if (Stage != LoadStage.Failed)
        Stage = LoadStage.Loaded;

      CurWait = new(Configs.AutoLoadWaitTime.Value, AutoLoad);
      await Platform.Wait(CurWait);
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
        Configs.AutoLoadOnStart.Value = AutoSort = !AutoSort;
      if (sortChanged || modsChanged)
      {
        modList.CheckDependencies();
        modList.DisableDuplicates(Configs.DisableDuplicates.Value);
        if (AutoSort)
          modList.SortByDeps();
        ModConfigUtil.SaveConfig(modList.ToModConfig());
      }
      var next = changed.HasFlag(ManualLoadWindow.ChangeFlags.NextStep);
      if (next)
        CurWait.Skip();
    }

    private static void StartGame()
    {
      Stage = LoadStage.Running;
      var co = (IEnumerator) typeof(SplashBehaviour).GetMethod("AwakeCoroutine", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(SplashBehaviour, new object[] { });
      SplashBehaviour.StartCoroutine(co);

      EssentialPatches.GameStarted = true;

      AlertPopup.Close();
      Platform.SetBackgroundEnabled(true);
    }

    public static string ExportModPackage()
    {
      try
      {
        var pkgpath = Path.Combine(LaunchPadPaths.SavePath, $"modpkg_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.zip");
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
              var entryPath = Path.Combine("mods", dirName, file.Substring(root.Length + 1)).Replace('\\', '/');
              archive.CreateEntryFromFile(file, entryPath);
            }
            config.Mods.Add(new LocalModData(dirName, true));
          }

          var configEntry = archive.CreateEntry("modconfig.xml");
          using (var stream = configEntry.Open())
          {
            var serializer = new XmlSerializer(typeof(ModConfig));
            serializer.Serialize(stream, config);
          }
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
  }
}
