using Assets.Scripts;
using Assets.Scripts.Networking.Transports;
using Assets.Scripts.Serialization;
using Assets.Scripts.Util;
using BepInEx.Bootstrap;
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
    Running,
    Failed,
  }

  public static class LaunchPadConfig
  {
    public static SplashBehaviour SplashBehaviour;
    public static List<ModInfo> Mods = new();

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
        await UniTask.WaitWhile(() => LoadState == LoadState.Configuring && !AutoLoadReady);
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
        await UniTask.WaitWhile(() => LoadState == LoadState.Loaded && !AutoLoadReady);
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
        LoadDetails();

        if (AutoSort)
          SortByDeps();

        Logger.Global.LogInfo("Mod Config Initialized");

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

    public static void Draw()
    {
      if (AutoLoad)
      {
        if (LaunchPadLoaderGUI.DrawAutoLoad(LoadState, SecondsUntilAutoLoad))
          AutoLoad = false;
      }
      else
      {
        var changed = LaunchPadLoaderGUI.DrawManualLoad(LoadState);
        HandleChange(changed);
      }

      LaunchPadAlertGUI.Draw();
    }

    private static void HandleChange(LaunchPadLoaderGUI.ChangeFlags changed)
    {
      if (changed == LaunchPadLoaderGUI.ChangeFlags.None)
        return;
      var sortChanged = changed.HasFlag(LaunchPadLoaderGUI.ChangeFlags.AutoSort);
      var modsChanged = changed.HasFlag(LaunchPadLoaderGUI.ChangeFlags.Mods);
      if (sortChanged)
        AutoSort = Configs.AutoLoadOnStart.Value;
      if (sortChanged || modsChanged)
      {
        if (AutoSort)
          SortByDeps();
        SaveConfig();
      }
      var next = changed.HasFlag(LaunchPadLoaderGUI.ChangeFlags.NextStep);
      if (next && LoadState == LoadState.Configuring)
        LoadState = LoadState.Loading;
      else if (next && LoadState == LoadState.Loaded)
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
      Mods.Add(new ModInfo { Source = ModSource.Core });
    }

    private static void LoadConfig()
    {
      var config = File.Exists(LaunchPadPaths.ConfigPath)
            ? XmlSerialization.Deserialize<ModConfig>(LaunchPadPaths.ConfigPath)
            : new ModConfig();
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
      var allItems = new List<Item>();
      var page = 1;
      const int batchSize = 5; // number of pages to fetch in parallel

      while (true)
      {
        // Prepare batch of pages
        var pageTasks = new List<UniTask<Item[]>>();
        for (var i = 0; i < batchSize; i++)
        {
          var currentPage = page + i;
          pageTasks.Add(FetchWorkshopPage(currentPage));
        }

        var results = await UniTask.WhenAll(pageTasks);
        var hasItems = false;
        foreach (var items in results)
        {
          if (items.Length > 0)
          {
            allItems.AddRange(items);
            hasItems = true;
          }
        }

        if (!hasItems)
          break; // no more pages

        page += batchSize;
      }

      // Determine which items need updates
      var needsUpdate = allItems.Where(item => item.NeedsUpdate || !Directory.Exists(item.Directory)).ToList();
      if (needsUpdate.Count > 0)
      {
        Logger.Global.Log($"Updating {needsUpdate.Count} workshop items");
        foreach (var item in needsUpdate)
          Logger.Global.LogInfo($"- {item.Title} ({item.Id})");

        await UniTask.WhenAll(needsUpdate.Select(item => item.DownloadAsync().AsUniTask()));
      }

      // Add all workshop mods
      foreach (var item in allItems)
      {
        Mods.Add(new ModInfo
        {
          Source = ModSource.Workshop,
          Wrapped = SteamTransport.ItemWrapper.WrapWorkshopItem(item, "About\\About.xml"),
          WorkshopItem = item
        });
      }
    }

    // Helper to fetch a single workshop page
    private static async UniTask<Item[]> FetchWorkshopPage(int page)
    {
      var query = Query.Items.WithTag("Mod");
      using var result = await query.AllowCachedResponse(0).WhereUserSubscribed().GetPageAsync(page);

      return !result.HasValue || result.Value.ResultCount == 0
        ? Array.Empty<Item>()
        : result.Value.Entries.Where(item => item.Result != Result.FileNotFound).ToArray();
    }

    private static void LoadDetails()
    {
      Mods.ForEach(LoadModDetails);
    }

    private static void LoadModDetails(ModInfo mod)
    {
      if (mod.Source == ModSource.Core)
        return;

      // Load About.xml once
      mod.About ??= XmlSerialization.Deserialize<ModAbout>(mod.Wrapped.FilePathFullName, "ModMetadata") ??
          new ModAbout
          {
            Name = $"[Invalid About.xml] {mod.Name}",
            Author = "",
            Version = "",
            Description = "",
          };

      var dllFiles = Directory.GetFiles(mod.Path, "*.dll", SearchOption.AllDirectories);
      foreach (var file in dllFiles)
      {
        mod.Assemblies.Add(new AssemblyInfo
        {
          Path = file,
          Definition = AssemblyDefinition.ReadAssembly(file, TypeLoader.ReaderParameters)
        });
      }

      var assetFiles = Directory.GetFiles(mod.Path, "*.assets", SearchOption.AllDirectories);
      mod.AssetBundles.AddRange(assetFiles);
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

      if (!config.SaveXml(LaunchPadPaths.ConfigPath))
        throw new Exception($"failed to save {WorkshopMenu.ConfigPath}");
    }

    private async static UniTask LoadMods()
    {
      var stopwatch = Stopwatch.StartNew();
      LoadState = LoadState.Loading;

      var (strategyType, strategyMode) = Configs.LoadStrategy;

      LoadStrategy loadStrategy = (strategyType, strategyMode) switch
      {
        (LoadStrategyType.Linear, LoadStrategyMode.Serial) => new LoadStrategyLinearSerial(),
        (LoadStrategyType.Linear, LoadStrategyMode.Parallel) => new LoadStrategyLinearParallel(),
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
