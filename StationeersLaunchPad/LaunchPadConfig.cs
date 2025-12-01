using Assets.Scripts;
using Assets.Scripts.Serialization;
using Assets.Scripts.Util;
using BepInEx.Configuration;
using Cysharp.Threading.Tasks;
using Steamworks;
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using UnityEngine;

namespace StationeersLaunchPad
{
  public enum LoadState
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

  public static class LaunchPadConfig
  {
    public static SplashBehaviour SplashBehaviour;

    private static ModList modList = new();

    private static LoadState LoadState = LoadState.Initializing;

    private static bool AutoSort;
    private static bool AutoLoad;
    private static bool SteamDisabled;

    private static Stopwatch AutoStopwatch = new();

    private static double SecondsUntilAutoLoad => Configs.AutoLoadWaitTime.Value - AutoStopwatch.Elapsed.TotalSeconds;
    private static bool AutoLoadReady => AutoLoad && SecondsUntilAutoLoad <= 0;

    public static async void Run(ConfigFile config, bool stopAutoLoad)
    {
      // we need to wait a frame so all the RuntimeInitializeOnLoad tasks are complete, otherwise GameManager.IsBatchMode won't be set yet
      await UniTask.Yield();

      Configs.Initialize(config);
      if (GameManager.IsBatchMode)
      {
        AutoLoad = true;
        SteamDisabled = true;
      }
      else
      {
        AutoLoad = !stopAutoLoad && Configs.AutoLoadOnStart.Value;
        SteamDisabled = Configs.DisableSteamOnStart.Value;
      }
      AutoSort = Configs.AutoSortOnStart.Value;

      // The save path on startup was used to load the mod list, so we can't change it at runtime.
      CustomSavePathPatches.SavePath = Configs.SavePathOnStart.Value;

      if (Configs.LinuxPathPatch.Value)
        LaunchPadPatches.RunLinuxPathPatch();

      await Load();

      if (!AutoLoad && GameManager.IsBatchMode)
      {
        Logger.Global.LogError("An error occurred during initialization. Exiting");
        Application.Quit();
      }

      if (!GameManager.IsBatchMode)
      {
        AutoStopwatch.Restart();
        await UniTaskX.WaitWhile(() => LoadState == LoadState.Configuring && !AutoLoadReady);
      }

      if (LoadState == LoadState.Configuring)
        LoadState = LoadState.Loading;

      if (LoadState == LoadState.Loading)
        await LoadMods();

      if (!AutoLoad && GameManager.IsBatchMode)
      {
        Logger.Global.LogError("An error occurred during mod loading. Exiting");
        Application.Quit();
      }

      if (!GameManager.IsBatchMode)
      {
        AutoStopwatch.Restart();
        await UniTaskX.WaitWhile(() => LoadState < LoadState.Running && !AutoLoadReady);
      }

      StartGame();
    }

    private static async UniTask Load()
    {
      try
      {
        if (Configs.RunPostUpdateCleanup)
        {
          LaunchPadUpdater.RunPostUpdateCleanup();
          Configs.PostUpdateCleanup.Value = false;
        }

        if (Configs.RunOneTimeBoosterInstall)
        {
          if (!await LaunchPadUpdater.RunOneTimeBoosterInstall())
            AutoLoad = false;
          Configs.OneTimeBoosterInstall.Value = false;
        }

        if (Configs.CheckForUpdate.Value)
        {
          LoadState = LoadState.Updating;
          if (await RunUpdate())
          {
            if (GameManager.IsBatchMode)
            {
              Logger.Global.LogWarning("LaunchPad has updated. Exiting");
              Application.Quit();
            }

            AutoLoad = false;
            LoadState = LoadState.Updating;

            await PostUpdateRestartDialog();
          }
        }

        LoadState = LoadState.Initializing;

        Logger.Global.LogInfo("Initializing...");
        await UniTask.RunOnThreadPool(() => Initialize());

        LoadState = LoadState.Searching;

        Logger.Global.LogInfo("Listing Local Mods");
        await modList.LoadLocalMods();

        if (!SteamDisabled)
        {
          Logger.Global.LogInfo("Listing Workshop Mods");
          await modList.LoadWorkshopMods();
        }

        Logger.Global.LogInfo("Loading Mod Order");
        await modList.LoadConfig();

        Logger.Global.LogInfo("Loading Details");
        modList.LoadDetails();

        if (!modList.CheckDependencies() && !GameManager.IsBatchMode)
          AutoLoad = false;

        if (modList.DisableDuplicates(Configs.DisableDuplicates.Value) && !GameManager.IsBatchMode)
          AutoLoad = false;

        if (AutoSort && !modList.SortByDeps() && !GameManager.IsBatchMode)
        {
          AutoSort = false;
          AutoLoad = false;
        }

        Logger.Global.LogInfo("Mod Config Initialized");

        LoadState = LoadState.Configuring;
      }
      catch (Exception ex)
      {
        if (!GameManager.IsBatchMode)
        {
          Logger.Global.LogError("Error occurred during initialization. Mods will not be loaded.");
          Logger.Global.LogException(ex);

          modList.Clear();
          LoadState = LoadState.Failed;
          AutoLoad = false;
        }
        else
        {
          Logger.Global.LogError("Error occurred during initialization.");
          Logger.Global.LogException(ex);
        }
      }
    }

    public static void Draw()
    {
      if (AutoLoad)
      {
        if (LaunchPadLoaderGUI.DrawAutoLoad(LoadState, SecondsUntilAutoLoad))
          AutoLoad = false;
      }
      else
      {
        var changed = LaunchPadLoaderGUI.DrawManualLoad(LoadState, modList, AutoSort);
        HandleChange(changed);
      }

      LaunchPadAlertGUI.Draw();
    }

    public static ModInfo MatchMod(ModData modData) =>
      modData != null ? modList.AllMods.First(mod => mod.Path == modData.DirectoryPath) : null;

    private static void HandleChange(LaunchPadLoaderGUI.ChangeFlags changed)
    {
      if (changed == LaunchPadLoaderGUI.ChangeFlags.None)
        return;
      var sortChanged = changed.HasFlag(LaunchPadLoaderGUI.ChangeFlags.AutoSort);
      var modsChanged = changed.HasFlag(LaunchPadLoaderGUI.ChangeFlags.Mods);
      if (sortChanged)
        AutoSort = Configs.AutoSortOnStart.Value;
      if (sortChanged || modsChanged)
      {
        modList.CheckDependencies();
        modList.DisableDuplicates(Configs.DisableDuplicates.Value);
        if (AutoSort)
          modList.SortByDeps();
        modList.SaveConfig();
      }
      var next = changed.HasFlag(LaunchPadLoaderGUI.ChangeFlags.NextStep);
      if (next && LoadState == LoadState.Configuring)
        LoadState = LoadState.Loading;
      else if (next && (LoadState == LoadState.Loaded || LoadState == LoadState.Failed))
        LoadState = LoadState.Running;
    }

    private static void Initialize()
    {
      Settings.CurrentData.SavePath = LaunchPadPaths.SavePath;

      if (!SteamDisabled)
      {
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
          AutoLoad = false;
          SteamDisabled = true;
        }
      }
      modList.AddCore();
    }

    private async static UniTask LoadMods()
    {
      var stopwatch = Stopwatch.StartNew();
      LoadState = LoadState.Loading;

      var (strategyType, strategyMode) = Configs.LoadStrategy;

      LoadStrategy loadStrategy = (strategyType, strategyMode) switch
      {
        (LoadStrategyType.Linear, LoadStrategyMode.Serial) => new LoadStrategyLinearSerial(modList),
        (LoadStrategyType.Linear, LoadStrategyMode.Parallel) => new LoadStrategyLinearParallel(modList),
        _ => throw new Exception($"invalid load strategy ({strategyType}, {strategyMode})")
      };
      if (!await loadStrategy.LoadMods())
        AutoLoad = false;

      stopwatch.Stop();
      Logger.Global.LogWarning($"Took {stopwatch.Elapsed:m\\:ss\\.fff} to load mods.");

      LoadState = LoadState.Loaded;
    }

    private async static UniTask<bool> RunUpdate()
    {
      try
      {
        Logger.Global.LogInfo("Checking Version");
        var release = await LaunchPadUpdater.GetUpdateRelease();
        if (release == null)
          return false;

        if (!Configs.AutoUpdateOnStart.Value && !await LaunchPadUpdater.CheckShouldUpdate(release))
          return false;

        if (!await LaunchPadUpdater.UpdateToRelease(release))
          return false;

        Logger.Global.LogError($"StationeersLaunchPad updated to {release.TagName}, please restart your game!");
        Configs.PostUpdateCleanup.Value = true;
        return true;
      }
      catch (Exception ex)
      {
        Logger.Global.LogError("An error occurred during update.");
        Logger.Global.LogException(ex);
        return false;
      }
    }

    private static void RestartGame()
    {
      var startInfo = new ProcessStartInfo
      {
        FileName = LaunchPadPaths.ExecutablePath,
        WorkingDirectory = LaunchPadPaths.GameRootPath,
        UseShellExecute = false
      };

      // remove environment variables that new process will inherit
      startInfo.Environment.Remove("DOORSTOP_INITIALIZED");
      startInfo.Environment.Remove("DOORSTOP_DISABLE");

      Process.Start(startInfo);
      Application.Quit();
    }

    private static UniTask PostUpdateRestartDialog()
    {
      bool restartGame()
      {
        RestartGame();
        return false;
      }
      bool stopLoad()
      {
        AutoLoad = false;
        return true;
      }
      return LaunchPadAlertGUI.Show(
        "Restart Recommended",
        "StationeersLaunchPad has been updated, it is recommended to restart the game.",
        LaunchPadAlertGUI.DefaultSize,
        LaunchPadAlertGUI.DefaultPosition,
        ("Restart Game", restartGame),
        ("Continue Loading", () => true),
        ("Close", stopLoad)
      );
    }

    private static void StartGame()
    {
      LoadState = LoadState.Running;
      var co = (IEnumerator) typeof(SplashBehaviour).GetMethod("AwakeCoroutine", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(SplashBehaviour, new object[] { });
      SplashBehaviour.StartCoroutine(co);

      EssentialPatches.GameStarted = true;

      LaunchPadAlertGUI.Close();
    }

    public static void ExportModPackage()
    {
      try
      {
        var pkgpath = Path.Combine(LaunchPadPaths.SavePath, $"modpkg_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.zip");
        using (var archive = ZipFile.Open(pkgpath, ZipArchiveMode.Create))
        {
          var config = new ModConfig();
          foreach (var mod in modList.EnabledMods)
          {
            if (mod.Source == ModSource.Core)
            {
              config.Mods.Add(new CoreModData());
              continue;
            }

            var dirName = $"{mod.Source}_{mod.Wrapped.DirectoryName}";
            var root = mod.Wrapped.DirectoryPath;
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
        Process.Start("explorer", $"/select,\"{pkgpath}\"");
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
      }
    }
  }
}
