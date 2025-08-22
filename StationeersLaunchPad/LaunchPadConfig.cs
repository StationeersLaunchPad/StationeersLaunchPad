using Assets.Scripts;
using Assets.Scripts.Networking.Transports;
using Assets.Scripts.Serialization;
using Assets.Scripts.Util;
using BepInEx;
using BepInEx.Configuration;
using Cysharp.Threading.Tasks;
using Mono.Cecil;
using Steamworks;
using Steamworks.Ugc;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using UnityEngine;
using BepInEx.Bootstrap;

namespace StationeersLaunchPad
{
  public enum LoadState
  {
    Initializing,
    Searching,
    Updating,
    Configuring,
    Loading,
    Loaded,
    Running,
    Updated,
    Failed,
  }

  public static class LaunchPadConfig
  {
    public static ConfigEntry<bool> DebugMode;
    public static ConfigEntry<bool> CheckForUpdate;
    public static ConfigEntry<bool> AutoUpdateOnStart;
    public static ConfigEntry<bool> AutoLoadOnStart;
    public static ConfigEntry<bool> AutoSortOnStart;
    public static ConfigEntry<int> AutoLoadWaitTime;
    public static ConfigEntry<LoadStrategyType> StrategyType;
    public static ConfigEntry<LoadStrategyMode> StrategyMode;
    public static ConfigEntry<bool> DisableSteam;
    public static ConfigEntry<string> SavePathOverride;
    public static ConfigEntry<bool> PostUpdateCleanup;
    public static ConfigEntry<bool> OneTimeBoosterInstall;
    public static ConfigEntry<bool> AutoScrollLogs;
    public static ConfigEntry<LogSeverity> LogSeverities;
    public static ConfigEntry<bool> CompactLogs;

    public static SortedConfigFile SortedConfig;

    public static SplashBehaviour SplashBehaviour;
    public static List<ModInfo> Mods = new();

    public static LoadState LoadState = LoadState.Initializing;
    public static LoadStrategyType LoadStrategyType = LoadStrategyType.Linear;
    public static LoadStrategyMode LoadStrategyMode = LoadStrategyMode.Serial;

    public static bool Debug = false;
    public static bool AutoSort = true;
    public static bool CheckUpdate = false;
    public static bool AutoUpdate = false;
    public static bool AutoLoad = true;
    public static bool HasUpdated = false;
    public static bool SteamDisabled = false;
    public static bool AutoScroll = false;
    public static LogSeverity Severities;
    public static string SavePath;
    public static Stopwatch AutoStopwatch = new();
    public static Stopwatch ElapsedStopwatch = new();

    private static void InitConfig(ConfigFile config)
    {
      DebugMode = config.Bind(
        new ConfigDefinition("Startup", "DebugMode"),
        false,
        new ConfigDescription(
          "If you run into issues with loading mods, or anything else, please enable this for more verbosity in error reports"
        )
      );
      AutoLoadOnStart = config.Bind(
        new ConfigDefinition("Startup", "AutoLoadOnStart"),
        true,
        new ConfigDescription(
          "Automatically load after the configured wait time on startup. Can be stopped by clicking the loading window at the bottom"
        )
       );
      CheckForUpdate = config.Bind(
        new ConfigDefinition("Startup", "CheckForUpdate"),
        !GameManager.IsBatchMode, // Default to false on DS
        new ConfigDescription(
          "Automatically check for mod loader updates on startup."
        )
      );
      AutoUpdateOnStart = config.Bind(
        new ConfigDefinition("Startup", "AutoUpdateOnStart"),
        !GameManager.IsBatchMode, // Default to false on DS
        new ConfigDescription(
          "Automatically update mod loader on startup. Ignored if CheckForUpdate is not also enabled."
        )
      );
      AutoLoadWaitTime = config.Bind(
        new ConfigDefinition("Startup", "AutoLoadWaitTime"),
        3,
        new ConfigDescription(
          "How many seconds to wait before loading mods, then loading the game",
          new AcceptableValueRange<int>(3, 30)
        )
      );
      AutoSortOnStart = config.Bind(
        new ConfigDefinition("Startup", "AutoSort"),
        true,
        new ConfigDescription(
          "Automatically sort based on LoadBefore/LoadAfter tags in mod data"
        )
      );
      DisableSteam = config.Bind(
        new ConfigDefinition("Startup", "DisableSteam"),
        false,
         new ConfigDescription(
          "Don't attempt to load steam workshop mods"
        )
      );
      StrategyType = config.Bind(
        new ConfigDefinition("Mod Loading", "LoadStrategyType"),
        LoadStrategyType.Linear,
        new ConfigDescription(
          "Linear type loads mods one by one in sequential order. More types of mod loading will be added later."
        )
      );
      StrategyMode = config.Bind(
        new ConfigDefinition("Mod Loading", "LoadStrategyMode"),
        LoadStrategyMode.Serial,
        new ConfigDescription(
          "Parallel mode loads faster for a large number of mods, but may fail in extremely rare cases. Switch to serial mode if running into loading issues."
        )
      );
      SavePathOverride = config.Bind(
        new ConfigDefinition("Mod Loading", "SavePathOverride"),
        "",
        new ConfigDescription(
          "This setting allows you to override the default path that config and save files are stored. Notice, due to how this path is implemented in the base game, this setting can only be applied on server start.  Changing it while in game will not have an effect until after a restart."
        )
      );
      AutoScrollLogs = config.Bind(
        new ConfigDefinition("Logging", "AutoScrollLogs"),
        true,
        new ConfigDescription(
          "This setting will automatically scroll when new lines are present if enabled."
        )
      );
      LogSeverities = config.Bind(
        new ConfigDefinition("Logging", "LogSeverities"),
        LogSeverity.All,
        new ConfigDescription(
          "This setting will filter what log severities will appear in the logging window."
        )
      );
      CompactLogs = config.Bind(
        new ConfigDefinition("Logging", "CompactLogs"),
        false,
        new ConfigDescription(
          "Omit extra information from logs displayed in game."
        )
      );
      PostUpdateCleanup = config.Bind(
        new ConfigDefinition("Internal", "PostUpdateCleanup"),
        true,
        new ConfigDescription(
          "This setting is automatically managed and should probably not be manually changed. Remove update backup files on start."
        )
      );
      OneTimeBoosterInstall = config.Bind(
        new ConfigDefinition("Internal", "OneTimeBoosterInstall"),
        true,
        new ConfigDescription(
          "This setting is automatically managed and should probably not be manually changed. Perform one-time download of LaunchPadBooster to update from version that didn't include it."
        )
      );

      SortedConfig = new SortedConfigFile(config);
    }

    public static async void Run(ConfigFile config)
    {
      // we need to wait a frame so all the RuntimeInitializeOnLoad tasks are complete, otherwise GameManager.IsBatchMode won't be set yet
      await UniTask.Yield();

      InitConfig(config);

      Debug = DebugMode.Value;
      AutoSort = AutoSortOnStart.Value;
      CheckUpdate = CheckForUpdate.Value;
      AutoUpdate = AutoUpdateOnStart.Value;
      AutoLoad = AutoLoadOnStart.Value || GameManager.IsBatchMode; // always autoload on server
      LoadStrategyType = StrategyType.Value;
      LoadStrategyMode = StrategyMode.Value;
      SavePath = SavePathOverride.Value;
      AutoScroll = AutoScrollLogs.Value;
      Severities = LogSeverities.Value;

      // steam is always disabled on dedicated servers
      SteamDisabled = DisableSteam.Value || GameManager.IsBatchMode;

      await Load();

      while (LoadState < LoadState.Updating)
        await UniTask.Yield();

      if (HasUpdated)
      {
        if (GameManager.IsBatchMode)
        {
          Logger.Global.LogWarning("LaunchPad has updated. Exiting");
          Application.Quit();
        }

        AutoLoad = false;

        await LaunchPadAlertGUI.Show("Restart Recommended", "StationeersLaunchPad has been updated, it is recommended to restart the game.",
          LaunchPadAlertGUI.DefaultSize,
          LaunchPadAlertGUI.DefaultPosition,
          ("Continue Loading", () =>
          {
            AutoLoad = true;

            return true;
          }
        ),
          ("Restart Game", () =>
          {
            var startInfo = new ProcessStartInfo();
            startInfo.FileName = Paths.ExecutablePath;
            startInfo.WorkingDirectory = Paths.GameRootPath;
            startInfo.UseShellExecute = false;

            // remove environment variables that new process will inherit
            startInfo.Environment.Remove("DOORSTOP_INITIALIZED");
            startInfo.Environment.Remove("DOORSTOP_DISABLE");

            Process.Start(startInfo);
            Application.Quit();

            return false;
          }
        ),
          ("Close", () => true)
        );
      }

      if (!AutoLoad && GameManager.IsBatchMode)
      {
        Logger.Global.LogError("An error occurred during initialization. Exiting");
        Application.Quit();
      }

      AutoStopwatch.Restart();
      while (LoadState == LoadState.Configuring && !GameManager.IsBatchMode && (!AutoLoad || AutoStopwatch.Elapsed.TotalSeconds < AutoLoadWaitTime.Value))
        await UniTask.Yield();

      if (LoadState == LoadState.Configuring)
        LoadState = LoadState.Loading;

      if (LoadState == LoadState.Loading)
        await LoadMods();

      if (!AutoLoad && GameManager.IsBatchMode)
      {
        Logger.Global.LogError("An error occurred during mod loading. Exiting");
        Application.Quit();
      }

      AutoStopwatch.Restart();
      while (LoadState == LoadState.Loaded && !GameManager.IsBatchMode && (!AutoLoad || AutoStopwatch.Elapsed.TotalSeconds < AutoLoadWaitTime.Value))
        await UniTask.Yield();

      StartGame();
    }

    private static async UniTask Load()
    {
      try
      {
        LoadState = LoadState.Initializing;

        Logger.Global.LogInfo("Initializing...");
        await UniTask.Run(() => Initialize());

        LoadState = LoadState.Searching;

        Logger.Global.LogInfo("Listing Local Mods");
        await UniTask.Run(() => LoadLocalItems());

        if (!SteamDisabled)
        {
          Logger.Global.LogInfo("Listing Workshop Mods");
          await LoadWorkshopItems();
        }

        Logger.Global.LogInfo("Loading Mod Order");
        await UniTask.Run(() => LoadConfig());

        Logger.Global.LogInfo("Loading Details");
        await LoadDetails();

        SortByDeps();

        Logger.Global.LogInfo("Mod Config Initialized");

        if (CheckUpdate && PostUpdateCleanup.Value)
          LaunchPadUpdater.RunPostUpdateCleanup();

        if (CheckUpdate && OneTimeBoosterInstall.Value)
          await LaunchPadUpdater.RunOneTimeBoosterInstall();

        if (CheckUpdate)
        {
          LoadState = LoadState.Updating;

          Logger.Global.LogInfo("Checking Version");
          await LaunchPadUpdater.CheckVersion();
        }

        LoadState = LoadState.Configuring;
      }
      catch (Exception ex)
      {
        if (!GameManager.IsBatchMode)
        {
          Logger.Global.LogError("Error occurred during initialization. Mods will not be loaded.");
          Logger.Global.LogException(ex);

          Mods = new();
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

    private static void Initialize()
    {
      if (string.IsNullOrEmpty(Settings.CurrentData.SavePath))
        Settings.CurrentData.SavePath = StationSaveUtils.DefaultPath;
      if (!SteamDisabled)
      {
        try
        {
          var transport = LaunchPadPatches.GetMetaServerTransport();
          transport.InitClient();
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
      Mods.Add(new ModInfo { Source = ModSource.Core });
    }

    private static void LoadConfig()
    {
      var path = WorkshopMenu.ConfigPath;
      var config = new ModConfig();
      if (File.Exists(path))
        config = XmlSerialization.Deserialize<ModConfig>(path);
      config.CreateCoreMod();

      var modsByPath = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
      foreach (var mod in Mods)
      {
        if (mod == null)
        {
          Logger.Global.LogWarning("Found Null mod in mods list.");
          continue;
        }

        //Speical case for core path
        if (mod.Source == ModSource.Core && string.IsNullOrEmpty(mod.Path))
        {
          modsByPath["Core"] = mod;
          continue;
        }

        if (string.IsNullOrEmpty(mod.Path))
        {
          Logger.Global.LogWarning($"Mod has empty path: {mod.GetType().Name}");
          continue;
        }
        var normalizedPath = NormalizePath(mod.Path);
        modsByPath[normalizedPath] = mod;
      }

      var localBasePath = SteamTransport.WorkshopType.Mod.GetLocalDirInfo().FullName;
      var newMods = new List<ModInfo>();

      foreach (var modcfg in config.Mods)
      {
        if (modcfg == null)
        {
          Logger.Global.LogWarning("Skipping null modcfg in config.");
          continue;
        }

        if (modcfg is CoreModData && string.IsNullOrEmpty(modcfg.DirectoryPath))
        {
          if (modsByPath.TryGetValue("Core", out var coreMod))
          {
            coreMod.Enabled = modcfg.Enabled;
            newMods.Add(coreMod);
            modsByPath.Remove("Core");
          }
          continue;
        }

        var modPath = (string) modcfg.DirectoryPath;
        if (!Path.IsPathRooted(modPath))
          modPath = Path.Combine(localBasePath, modPath);

        var normalizedModPath = NormalizePath(modPath);
        if (string.IsNullOrEmpty(normalizedModPath))
        {
          Logger.Global.LogWarning($"Invalid path in mod config: {modcfg.GetType().Name}");
          continue;
        }

        if (modsByPath.TryGetValue(normalizedModPath, out var mod))
        {
          mod.Enabled = modcfg.Enabled;
          newMods.Add(mod);
          modsByPath.Remove(normalizedModPath);
        }
        else if (modcfg.Enabled)
        {
          Logger.Global.LogWarning($"enabled mod not found at {modPath}");
        }
      }
      foreach (var mod in modsByPath.Values)
      {
        Logger.Global.LogDebug($"new mod added at {mod.Path}");
        newMods.Add(mod);
        mod.Enabled = true;
      }
      Mods = newMods;
      SaveConfig();
    }

    private static string NormalizePath(string path)
    {
      return path?.Replace("\\", "/").Trim().ToLowerInvariant() ?? string.Empty;
    }

    private static void LoadLocalItems()
    {
      var type = SteamTransport.WorkshopType.Mod;
      var localDir = type.GetLocalDirInfo();
      var fileName = type.GetLocalFileName();

      if (!localDir.Exists)
      {
        Logger.Global.LogWarning("local mod folder not found");
        return;
      }

      foreach (var dir in localDir.GetDirectories("*", SearchOption.AllDirectories))
      {
        foreach (var file in dir.GetFiles(fileName))
        {
          Mods.Add(new ModInfo()
          {
            Source = ModSource.Local,
            Wrapped = SteamTransport.ItemWrapper.WrapLocalItem(file, type),
          });
        }
      }
    }

    private static async UniTask LoadWorkshopItems()
    {
      var items = new List<Item>();
      for (var page = 1; ; page++)
      {
        var query = Query.Items.WithTag("Mod");
        using var result = await query.AllowCachedResponse(0).WhereUserSubscribed().GetPageAsync(page);

        if (!result.HasValue || result.Value.ResultCount == 0)
          break;

        // filter out deleted items
        items.AddRange(result.Value.Entries.Where(item => item.Result != Result.FileNotFound));
      }

      var needsUpdate = items.Where(item => item.NeedsUpdate || !Directory.Exists(item.Directory)).ToList();
      if (needsUpdate.Count > 0)
      {
        Logger.Global.Log($"Updating {needsUpdate.Count} workshop items");
        foreach (var item in needsUpdate)
          Logger.Global.Log($"- {item.Title}({item.Id})");
        await UniTask.WhenAll(needsUpdate.Select(item => item.DownloadAsync().AsUniTask()));
      }

      foreach (var item in items)
      {
        Mods.Add(new ModInfo()
        {
          Source = ModSource.Workshop,
          Wrapped = SteamTransport.ItemWrapper.WrapWorkshopItem(item, "About\\About.xml"),
          WorkshopItem = item,
        });
      }
    }

    private static async UniTask LoadDetails()
    {
      await UniTask.WhenAll(Mods.Select(mod => UniTask.Run(() => LoadModDetails(mod))));
    }

    private static void LoadModDetails(ModInfo mod)
    {
      if (mod.Source == ModSource.Core)
        return;

      mod.About = XmlSerialization.Deserialize<ModAbout>(mod.Wrapped.FilePathFullName, "ModMetadata") ??
        new ModAbout
        {
          Name = $"[Invalid About.xml] {mod.Wrapped.DirectoryName}",
          Author = "",
          Version = "",
          Description = "",
        };

      foreach (var file in Directory.GetFiles(mod.Wrapped.DirectoryPath, "*.dll", SearchOption.AllDirectories))
      {
        var def = AssemblyDefinition.ReadAssembly(file, TypeLoader.ReaderParameters);
        mod.Assemblies.Add(new()
        {
          Path = file,
          Definition = def,
          Name = def.Name.Name,
        });
      }

      foreach (var file in Directory.GetFiles(mod.Wrapped.DirectoryPath, "*.assets", SearchOption.AllDirectories))
      {
        mod.AssetBundles.Add(file);
      }
    }

    public static void SortByDeps()
    {
      var beforeDeps = new Dictionary<int, List<int>>();
      var modsById = new Dictionary<ulong, int>();
      void addDep(int from, int to)
      {
        if (from == to)
          return;
        var list = beforeDeps.GetValueOrDefault(from) ?? new();
        if (!list.Contains(to))
          list.Add(to);
        beforeDeps[from] = list;
      }
      for (var i = 0; i < Mods.Count; i++)
      {
        var mod = Mods[i];
        mod.SortIndex = i;
        if (!mod.Enabled)
        {
          mod.DepsWarned = false;
          continue;
        }

        if (mod.Source == ModSource.Core)
          modsById[1] = mod.SortIndex;
        else if (mod.About.WorkshopHandle != 0)
          modsById[mod.About.WorkshopHandle] = i;
      }
      foreach (var mod in Mods)
      {
        if (!mod.Enabled)
          continue;
        if (mod.Source == ModSource.Core)
          continue;
        bool missingDeps = false;
        foreach (var dep in mod.About.Dependencies ?? new())
          if (!modsById.ContainsKey(dep.Id))
          {
            missingDeps = true;
            if (!mod.DepsWarned)
            {
              Logger.Global.LogWarning($"{mod.Source} {mod.DisplayName} is missing dependency with workshop id {dep.Id}");
              var found = false;
              foreach (var mod2 in Mods)
              {
                if (mod2.About?.WorkshopHandle == dep.Id)
                {
                  if (!found)
                    Logger.Global.LogWarning("Possible matches:");
                  found = true;
                  Logger.Global.LogWarning($"- {mod2.Source} {mod2.DisplayName}");
                }
              }
              if (!found)
                Logger.Global.LogWarning("No possible matches installed");
            }
          }
        mod.DepsWarned = missingDeps;

        // LoadBefore and LoadAfter inherited from StationeersMods are the opposite of what you would expect.
        // LoadBefore is other mods that should be loaded before this one.
        // LoadAfter is other mods that should be loaded after this one.
        foreach (var before in mod.About.LoadBefore ?? new())
          if (modsById.TryGetValue(before.Id, out var beforeIndex))
            addDep(mod.SortIndex, beforeIndex); // before before mod
        foreach (var after in mod.About.LoadAfter ?? new())
          if (modsById.TryGetValue(after.Id, out var afterIndex))
            addDep(afterIndex, mod.SortIndex); // mod before after
      }

      var visited = new bool[Mods.Count];
      var circularList = new List<int>();
      bool checkCircular(int index)
      {
        if (visited[index])
        {
          circularList.Add(index);
          return true;
        }
        visited[index] = true;
        foreach (var before in beforeDeps.GetValueOrDefault(index) ?? new())
        {
          if (checkCircular(before))
          {
            circularList.Add(index);
            return true;
          }
        }
        visited[index] = false;
        return false;
      }

      var foundCircular = false;
      foreach (var mod in Mods)
      {
        if (!mod.Enabled)
          continue;

        if (checkCircular(mod.SortIndex))
        {
          foundCircular = true;
          break;
        }
      }

      if (foundCircular)
      {
        Logger.Global.LogError("Circular dependency found in enabled mods:");
        for (var i = circularList.Count - 1; i >= 0; i--)
        {
          var mod = Mods[circularList[i]];
          Logger.Global.LogError($"- {mod.Source} {mod.DisplayName}");
        }
        AutoLoad = false;
        AutoSort = false;
        return;
      }

      // loop over all mods repeatedly, adding any who are either disabled or have all their dependencies met.
      // this makes this an n^2 sort worst case. while we could likely do better on this complexity, this approach is simple and
      // has a negligible runtime in up to hundreds of mods.
      var added = new bool[Mods.Count];
      var newOrder = new List<ModInfo>();
      bool areDepsAdded(int index)
      {
        if (!beforeDeps.TryGetValue(index, out var befores))
          return true; // has no deps

        foreach (var bindex in befores)
          if (!added[bindex])
            return false;

        return true;
      }

      while (true)
      {
        var delayed = false;
        var progress = false;
        foreach (var mod in Mods)
        {
          if (added[mod.SortIndex])
            continue;
          if (!mod.Enabled || areDepsAdded(mod.SortIndex))
          {
            added[mod.SortIndex] = true;
            newOrder.Add(mod);
            progress = true;
            if (delayed)
              break; // if we skipped any this iteration, stop as soon as we add a new mod so we don't push others too far forward
          }
          else
            delayed = true;
        }
        if (!progress)
          break;
      }

      // at this point just add any mods we haven't added yet. we already checked for circular dependencies above so this should
      // only do anything if there is a bug above.
      foreach (var mod in Mods)
      {
        if (!added[mod.SortIndex])
          newOrder.Add(mod);
      }

      Mods = newOrder;
    }

    public static void SaveConfig()
    {
      var config = new ModConfig();
      foreach (var mod in Mods)
      {
        config.Mods.Add(mod.Source switch
        {
          ModSource.Core => new CoreModData(),
          ModSource.Local => new LocalModData(mod.Path, mod.Enabled),
          ModSource.Workshop => new WorkshopModData(mod.Wrapped, mod.Enabled),
          _ => throw new Exception($"invalid mod source: {mod.Source}"),
        });
      }

      if (!config.SaveXml(WorkshopMenu.ConfigPath))
        throw new Exception($"failed to save {WorkshopMenu.ConfigPath}");
    }

    private async static UniTask LoadMods()
    {
      ElapsedStopwatch.Restart();
      LoadState = LoadState.Loading;

      LaunchPadLoaderGUI.SelectedInfo = null;

      LoadStrategy loadStrategy = (LoadStrategyType, LoadStrategyMode) switch
      {
        (LoadStrategyType.Linear, LoadStrategyMode.Serial) => new LoadStrategyLinearSerial(),
        (LoadStrategyType.Linear, LoadStrategyMode.Parallel) => new LoadStrategyLinearParallel(),
        _ => throw new Exception($"invalid load strategy ({LoadStrategyType}, {LoadStrategyMode})")
      };
      await loadStrategy.LoadMods();

      ElapsedStopwatch.Stop();
      Logger.Global.LogWarning($"Took {ElapsedStopwatch.Elapsed.ToString(@"m\:ss\.fff")} to load mods.");

      LoadState = LoadState.Loaded;
    }

    private static void StartGame()
    {
      LoadState = LoadState.Running;
      var co = (IEnumerator) typeof(SplashBehaviour).GetMethod("AwakeCoroutine", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(SplashBehaviour, new object[] { });
      SplashBehaviour.StartCoroutine(co);

      LaunchPadLoaderGUI.IsActive =
      LaunchPadConsoleGUI.IsActive =
      LaunchPadAlertGUI.IsActive =
      LaunchPadConfigGUI.IsActive = false;
    }

    public static void ExportModPackage()
    {
      try
      {
        var pkgpath = Path.Combine(StationSaveUtils.DefaultPath, $"modpkg_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.zip");
        using (var archive = ZipFile.Open(pkgpath, ZipArchiveMode.Create))
        {
          var config = new ModConfig();
          foreach (var mod in Mods)
          {
            if (!mod.Enabled)
              continue;
            if (mod.Source == ModSource.Core)
            {
              config.Mods.Add(new CoreModData());
              continue;
            }

            var dirName = $"{mod.Source}_{mod.Wrapped.DirectoryName}";
            var root = mod.Wrapped.DirectoryPath;
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
              var entryPath = Path.Combine("mods", dirName, file.Substring(root.Length + 1));
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