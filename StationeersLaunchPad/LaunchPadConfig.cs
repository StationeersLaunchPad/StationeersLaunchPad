using Assets.Scripts;
using Assets.Scripts.Networking.Transports;
using Assets.Scripts.Serialization;
using Assets.Scripts.Util;
using BepInEx;
using BepInEx.Configuration;
using Cysharp.Threading.Tasks;
using ImGuiNET;
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
using UI.ImGuiUi;
using UnityEngine;
using static UnityEngine.EventSystems.EventTrigger;

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

    public static SortedConfigFile SortedConfig;

    public static SplashBehaviour SplashBehaviour;
    public static List<ModInfo> Mods = new();
    public static HashSet<string> GameAssemblies = new();

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

    public static async void Run()
    {
      Debug = DebugMode.Value;
      AutoSort = AutoSortOnStart.Value;
      CheckUpdate = CheckForUpdate.Value;
      AutoUpdate = AutoUpdateOnStart.Value;
      AutoLoad = AutoLoadOnStart.Value;
      LoadStrategyType = StrategyType.Value;
      LoadStrategyMode = StrategyMode.Value;
      SavePath = SavePathOverride.Value;
      AutoScroll = AutoScrollLogs.Value;
      Severities = LogSeverities.Value;

      // we need to wait a frame so all the RuntimeInitializeOnLoad tasks are complete, otherwise GameManager.IsBatchMode won't be set yet
      await UniTask.Yield();

      // steam is always disabled on dedicated servers
      SteamDisabled = DisableSteam.Value || GameManager.IsBatchMode;

      await Load();

      while (LoadState < LoadState.Updating)
        await UniTask.Yield();

      if (HasUpdated && !GameManager.IsBatchMode)
      {
        AutoLoad = false;

        await LaunchPadAlertGUI.Show("Restart Recommended", "StationeersLaunchPad has been updated, it is recommended to restart the game.",
          LaunchPadAlertGUI.DefaultSize,
          LaunchPadAlertGUI.DefaultPosition,
          ("Continue Loading", () => {
            AutoLoad = true;

            return true;
          }),
          ("Restart Game", () => {
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
          }),
          ("Close", () => true)
        );
      }

      AutoStopwatch.Restart();
      while (LoadState == LoadState.Configuring && (!AutoLoad || AutoStopwatch.Elapsed.TotalSeconds < AutoLoadWaitTime.Value))
        await UniTask.Yield();

      if (LoadState == LoadState.Configuring)
        LoadState = LoadState.Loading;

      if (LoadState == LoadState.Loading)
        await LoadMods();

      AutoStopwatch.Restart();
      while (LoadState == LoadState.Loaded && (!AutoLoad || AutoStopwatch.Elapsed.TotalSeconds < AutoLoadWaitTime.Value))
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

        Logger.Global.LogInfo("Listing Game Assemblies");
        await UniTask.Run(() => LoadGameAssemblies());

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
      if (!SteamClient.IsValid && !SteamDisabled)
      {
        try
        {
          SteamClient.Init(SteamTransport.APP_ID);
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
      for (var page = 1; ; page++)
      {
        var query = Query.Items.WithTag("Mod");
        var result = await query.AllowCachedResponse(0).WhereUserSubscribed().GetPageAsync(page);

        if (!result.HasValue || result.Value.ResultCount == 0)
          break;

        foreach (var item in result.Value.Entries)
        {
          Mods.Add(new ModInfo()
          {
            Source = ModSource.Workshop,
            Wrapped = SteamTransport.ItemWrapper.WrapWorkshopItem(item, "About\\About.xml"),
            WorkshopItem = item,
          });
        }
      }
    }

    private static void LoadGameAssemblies()
    {
      foreach (var file in Directory.EnumerateFiles(AppDomain.CurrentDomain.BaseDirectory, "*.dll", SearchOption.AllDirectories))
      {
        try
        {
          using var def = AssemblyDefinition.ReadAssembly(file);
          GameAssemblies.Add(def.Name.Name);
        }
        catch (BadImageFormatException)
        {
          // silently ignore
        }
        catch (Exception ex)
        {
          Logger.Global.LogError($"error reading game assembly {file}: {ex.Message}");
        }
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
        var info = new AssemblyInfo { Path = file, Dependencies = new() };

        using (var def = AssemblyDefinition.ReadAssembly(file))
        {
          info.Name = def.Name.Name;
          foreach (var module in def.Modules)
            foreach (var mref in module.AssemblyReferences)
              if (!GameAssemblies.Contains(mref.Name))
                info.Dependencies.Add(mref.Name);
        }
        mod.Assemblies.Add(info);
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

      IsActive = false;
      LaunchPadLoader.IsActive = false;
      LaunchPadConsole.IsActive = false;
      LaunchPadAlertGUI.IsActive = false;
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

    public static bool IsActive = false;

    public static void Draw()
    {
      ImGuiHelper.Draw(() => DrawConfigEditor());
    }

    public static bool ConfigChanged = false;
    private static ModInfo draggingMod = null;
    private static int draggingIndex = -1;
    public static void DrawConfigTable(bool edit = false)
    {
      if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        draggingMod = null;

      var hoveringIndex = -1;
      if (draggingMod != null)
        draggingIndex = Mods.IndexOf(draggingMod);

      if (!edit)
        ImGui.BeginDisabled();

      if (ImGui.BeginTable("##configtable", 3, ImGuiTableFlags.SizingFixedFit))
      {
        ImGui.TableSetupColumn("##enabled");
        ImGui.TableSetupColumn("##type");
        ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthStretch);

        for (var i = 0; i < Mods.Count; i++)
        {
          var mod = Mods[i];

          ImGui.TableNextRow();
          ImGui.PushID(i);
          ImGui.TableNextColumn();

          if (ImGui.Checkbox("Enabled", ref mod.Enabled))
            ConfigChanged = true;

          ImGui.TableNextColumn();
          ImGui.Selectable($"##rowdrag", mod == draggingMod, ImGuiSelectableFlags.SpanAllColumns);
          if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem) && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && draggingMod == null)
          {
            hoveringIndex = draggingIndex = i;
            draggingMod = mod;
          }

          ImGuiHelper.DrawSameLine(() => ImGuiHelper.Text($"{mod.Source}"));

          ImGui.TableNextColumn();
          ImGuiHelper.Text($"{mod.DisplayName}");

          if (draggingMod != null)
            if (mod.SortBefore(draggingMod))
              ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextRightDisabled("Before"));
            else if (draggingMod.SortBefore(mod))
              ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextRightDisabled("After"));

          ImGui.PopID();
        }
        ImGui.EndTable();
      }

      if (!edit)
        ImGui.EndDisabled();

      if (edit && draggingIndex != -1 && hoveringIndex != -1 && draggingIndex != hoveringIndex)
      {
        while (draggingIndex < hoveringIndex)
        {
          (Mods[draggingIndex + 1], Mods[draggingIndex]) = (Mods[draggingIndex], Mods[draggingIndex + 1]);
          draggingIndex++;
        }

        while (draggingIndex > hoveringIndex)
        {
          (Mods[draggingIndex - 1], Mods[draggingIndex]) = (Mods[draggingIndex], Mods[draggingIndex - 1]);
          draggingIndex--;
        }

        ConfigChanged = true;
      }
    }

    public static void DrawWorkshopConfig(ModData modData)
    {
      if (modData == null)
        LaunchPadLoader.SelectedInfo = null;
      else if (LaunchPadLoader.SelectedMod == null || LaunchPadLoader.SelectedInfo.Path != modData.DirectoryPath)
        LaunchPadLoader.SelectedInfo = Mods.Find(mod => mod.Path == modData.DirectoryPath);

      var screenSize = ImguiHelper.ScreenSize;
      var padding = new Vector2(25, 25);
      var topLeft = new Vector2(screenSize.x - 800f - padding.x, padding.y);
      var bottomRight = screenSize - padding;

      ImGuiHelper.Draw(() =>
      {
        ImGui.SetNextWindowSize(bottomRight - topLeft);
        ImGui.SetNextWindowPos(topLeft);
        if (ImGui.Begin("Mod Configuration##menuconfig", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings))
          DrawConfigEditor();
        ImGui.End();
      });
    }

    public static void DrawConfigEditor()
    {
      if (LaunchPadLoader.SelectedInfo == null || LaunchPadLoader.SelectedInfo.Source == ModSource.Core)
      {
        ImGuiHelper.TextDisabled("Select a mod to edit configuration");
        return; 
      }

      if (LaunchPadLoader.SelectedMod == null)
      {
        ImGuiHelper.TextDisabled("Mod was not enabled at load time or is not configurable.");
        return;
      }

      var configFiles = LaunchPadLoader.SelectedMod.GetSortedConfigs();
      if (configFiles == null || configFiles.Count == 0)
      {
        ImGuiHelper.TextDisabled($"{LaunchPadLoader.SelectedInfo.DisplayName} does not have any configuration");
        return;
      }

      ImGuiHelper.TextDisabled("These configurations may require a restart to apply");
      ImGui.BeginChild("##config", ImGuiWindowFlags.HorizontalScrollbar);
      foreach (var configFile in configFiles)
      {
        DrawConfigFile(configFile);
      }
      ImGui.EndChild();
    }

    public static void DrawConfigFile(SortedConfigFile configFile, Func<string, bool> except = null)
    {
      ImGuiHelper.Text(configFile.FileName);
      ImGui.PushID(configFile.FileName);

      foreach (var category in configFile.Categories)
      {
        if (except?.Invoke(category.Category) ?? false)
          continue;

        if (!ImGui.CollapsingHeader(category.Category, ImGuiTreeNodeFlags.DefaultOpen))
          continue;

        foreach (var entry in category.Entries)
        {
           if (DrawConfigEntry(entry))
            ConfigChanged = true;
        }
      }

      ImGui.PopID();
    }

    public static bool DrawConfigEntry(ConfigEntryBase entry, bool fill = true)
    {
      var changed = false;
      ImGui.PushID(entry.Definition.Key);
      ImGui.BeginGroup();

      ImGuiHelper.Text(entry.Definition.Key);
      ImGui.SameLine();
      if (fill)
        ImGui.SetNextItemWidth(-float.Epsilon);

      var value = entry.BoxedValue;
      changed = value switch
      {
        Color => DrawColorEntry(entry as ConfigEntry<Color>),
        Vector2 => DrawVector2Entry(entry as ConfigEntry<Vector2>),
        Vector3 => DrawVector3Entry(entry as ConfigEntry<Vector3>),
        Vector4 => DrawVector4Entry(entry as ConfigEntry<Vector4>),

        Enum => DrawEnumEntry(entry, value as Enum),
        string => DrawStringEntry(entry as ConfigEntry<string>),
        char => DrawCharEntry(entry as ConfigEntry<char>),
        bool => DrawBoolEntry(entry as ConfigEntry<bool>),
        float => DrawFloatEntry(entry as ConfigEntry<float>),
        double => DrawDoubleEntry(entry as ConfigEntry<double>),
        decimal => DrawDecimalEntry(entry as ConfigEntry<decimal>),
        byte => DrawByteEntry(entry as ConfigEntry<byte>),
        sbyte => DrawSByteEntry(entry as ConfigEntry<sbyte>),
        short => DrawShortEntry(entry as ConfigEntry<short>),
        ushort => DrawUShortEntry(entry as ConfigEntry<ushort>),
        int => DrawIntEntry(entry as ConfigEntry<int>),
        uint => DrawUIntEntry(entry as ConfigEntry<uint>),
        long => DrawLongEntry(entry as ConfigEntry<long>),
        ulong => DrawULongEntry(entry as ConfigEntry<ulong>),
        _ => DrawDefault(entry),
      };

      ImGui.EndGroup();
      ImGui.PopID();

      var description = entry.Description?.Description;
      if (!string.IsNullOrEmpty(description))
        ImGuiHelper.ItemTooltip(description, 600f);
      return changed;
    }

    private static bool DrawColorEntry(ConfigEntry<Color> entry)
    {
      var changed = false;

      var value = entry.Value;
      var r = value.r;
      ImGui.Spacing();
      ImGuiHelper.Text($"Red ({r * 255}):");
      if (ImGui.SliderFloat("##colorvaluer", ref r, 0.0f, 1.0f))
      {
        entry.BoxedValue = new Color(r, value.g, value.b, value.a);
        changed = true;
      }

      var g = value.g;
      ImGuiHelper.Text($"Green ({g * 255}):");
      if (ImGui.SliderFloat("##colorvalueg", ref g, 0.0f, 1.0f))
      {
        entry.BoxedValue = new Color(value.r, g, value.b, value.a);
        changed = true;
      }

      var b = value.b;
      ImGuiHelper.Text($"Blue ({b * 255}):");
      if (ImGui.SliderFloat("##colorvalueb", ref b, 0.0f, 1.0f))
      {
        entry.BoxedValue = new Color(value.r, value.g, b, value.a);
        changed = true;
      }

      var a = value.a;
      ImGuiHelper.Text($"Alpha ({a * 255}):");
      if (ImGui.SliderFloat("##colorvaluea", ref a, 0.0f, 1.0f))
      {
        entry.BoxedValue = new Color(value.r, value.g, value.b, a);
        changed = true;
      }

      return changed;
    }

    private static bool DrawVector2Entry(ConfigEntry<Vector2> entry)
    {
      var changed = false;

      var value = entry.Value;
      var x = value.x;
      ImGui.Spacing();
      ImGuiHelper.Text("X:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector2valuex", ref x))
      {
        entry.BoxedValue = new Vector2(x, value.y);
        changed = true;
      }

      var y = value.y;
      ImGuiHelper.Text("Y:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector2valuey", ref y))
      {
        entry.BoxedValue = new Vector2(value.x, y);
        changed = true;
      }

      return changed;
    }

    private static bool DrawVector3Entry(ConfigEntry<Vector3> entry)
    {
      var changed = false;

      var value = entry.Value;
      var x = value.x;
      ImGui.Spacing();
      ImGuiHelper.Text("X:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector3valuex", ref x))
      {
        entry.BoxedValue = new Vector3(x, value.y, value.z);
        changed = true;
      }

      var y = value.y;
      ImGuiHelper.Text("Y:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector3valuey", ref y))
      {
        entry.BoxedValue = new Vector3(value.x, y, value.z);
        changed = true;
      }

      var z = value.z;
      ImGuiHelper.Text("Z:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector3valuez", ref z))
      {
        entry.BoxedValue = new Vector3(value.x, value.y, z);
        changed = true;
      }

      return changed;
    }

    private static bool DrawVector4Entry(ConfigEntry<Vector4> entry)
    {
      var changed = false;

      var value = entry.Value;
      var x = value.x;
      ImGui.Spacing();
      ImGuiHelper.Text("X:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector4valuex", ref x))
      {
        entry.BoxedValue = new Vector4(x, value.y, value.z, value.w);
        changed = true;
      }

      var y = value.y;
      ImGuiHelper.Text("Y:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector4valuey", ref y))
      {
        entry.BoxedValue = new Vector4(value.x, y, value.z, value.w);
        changed = true;
      }

      var z = value.z;
      ImGuiHelper.Text("Z:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector4valuez", ref z))
      {
        entry.BoxedValue = new Vector4(value.x, value.y, z, value.w);
        changed = true;
      }

      var w = value.z;
      ImGuiHelper.Text("W:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector4valuew", ref w))
      {
        entry.BoxedValue = new Vector4(value.x, value.y, value.z, w);
        changed = true;
      }

      return changed;
    }

    public static bool DrawEnumEntry(ConfigEntryBase entry, Enum value)
    {
      var changed = false;
      var currentValue = Convert.ToInt32(value);
      var type = value.GetType();
      var values = Enum.GetValues(type);
      var names = Enum.GetNames(type);
      var index = -1;
      for (var i = 0; i < values.Length; i++)
      {
        if (values.GetValue(i).Equals(value))
        {
          index = i;
          break;
        }
      }

      if (type.GetCustomAttribute<FlagsAttribute>() != null)
      {
        for (var i = 0; i < values.Length; i++)
        {
          for (; i < values.Length; i++)
          {
            var val = (int) values.GetValue(i);
            var newValue = (currentValue & val) == val;
            if (ImGui.Checkbox(names.GetValue(i).ToString(), ref newValue))
            {
              entry.BoxedValue = newValue ? currentValue | val : currentValue & ~val;
              changed = true;
            }
            if (i != values.Length - 1)
              ImGui.SameLine();
          }
        } 
      }
      else
      {
        if (ImGui.Combo("##enumvalue", ref index, names, names.Length))
        {
          entry.BoxedValue = values.GetValue(index);
          changed = true;
        }
      }

      return changed;
    }

    public static bool DrawStringEntry(ConfigEntry<string> entry)
    {
      var changed = false;
      var value = entry.Value;
      if (ImGui.InputText("##stringvalue", ref value, uint.MaxValue, ImGuiInputTextFlags.EnterReturnsTrue) || ImGui.IsItemDeactivatedAfterEdit())
      {
        entry.BoxedValue = value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawCharEntry(ConfigEntry<char> entry)
    {
      var changed = false;
      var value = $"{entry.Value}";
      if (ImGui.InputText("##charvalue", ref value, 1, ImGuiInputTextFlags.EnterReturnsTrue) || ImGui.IsItemDeactivatedAfterEdit())
      {
        entry.BoxedValue = value[0];
        changed = true;
      }

      return changed;
    }

    public static bool DrawBoolEntry(ConfigEntry<bool> entry)
    {
      var changed = false;

      var value = entry.Value;
      if (ImGui.Checkbox("##boolvalue", ref value))
      {
        entry.BoxedValue = value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawFloatEntry(ConfigEntry<float> entry)
    {
      var changed = false;

      var value = entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<float> valueRange)
      {
        if (ImGui.SliderFloat("##floatvalue", ref value, valueRange.MinValue, valueRange.MaxValue))
        {
          entry.BoxedValue = value;
          changed = true;
        }
      }
      else if (ImGui.InputFloat("##floatvalue", ref value, step: 0))
      {
        entry.BoxedValue = value;
        changed = true;
      }

      return changed;
    }

    public static unsafe bool DrawDoubleEntry(ConfigEntry<double> entry)
    {
      var changed = false;

      var value = entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<double> valueRange)
      {
        var min = valueRange.MinValue;
        var max = valueRange.MaxValue;
        if (ImGui.SliderScalar("##doublevalue", ImGuiDataType.Double, (IntPtr) (&value), (IntPtr) (&min), (IntPtr) (&max)))
        {
          entry.BoxedValue = value;
          changed = true;
        }
      }
      else if (ImGui.InputDouble("##doublevalue", ref value, step: 0))
      {
        entry.BoxedValue = value;
        changed = true;
      }

      return changed;
    }

    public static unsafe bool DrawDecimalEntry(ConfigEntry<decimal> entry)
    {
      var changed = false;

      var value = (double) entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<decimal> valueRange)
      {
        var min = valueRange.MinValue;
        var max = valueRange.MaxValue;
        if (ImGui.SliderScalar("##decimalvalue", ImGuiDataType.Double, (IntPtr) (&value), (IntPtr) (&min), (IntPtr) (&max)))
        {
          entry.BoxedValue = value;
          changed = true;
        }
      }
      else if (ImGui.InputDouble("##decimalvalue", ref value, step: 0))
      {
        entry.BoxedValue = value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawByteEntry(ConfigEntry<byte> entry)
    {
      var changed = false;

      var value = (int) entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<byte> valueRange)
      {
        if (ImGui.SliderInt("##bytevalue", ref value, valueRange.MinValue, valueRange.MaxValue))
        {
          entry.BoxedValue = (byte) value;
          changed = true;
        }
      }
      else if (ImGui.InputInt("##bytevalue", ref value, step: 0))
      {
        entry.BoxedValue = (byte) value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawSByteEntry(ConfigEntry<sbyte> entry)
    {
      var changed = false;
      var value = (int) entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<sbyte> valueRange)
      {
        if (ImGui.SliderInt("##sbytevalue", ref value, valueRange.MinValue, valueRange.MaxValue))
        {
          entry.BoxedValue = (sbyte) value;
          changed = true;
        }
      }
      else if (ImGui.InputInt("##sbytevalue", ref value, step: 0))
      {
        entry.BoxedValue = (sbyte) value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawShortEntry(ConfigEntry<short> entry)
    {
      var changed = false;

      var value = (int) entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<short> valueRange)
      {
        if (ImGui.SliderInt("##shortvalue", ref value, valueRange.MinValue, valueRange.MaxValue))
        {
          entry.BoxedValue = (short) value;
          changed = true;
        }
      }
      else if (ImGui.InputInt("##shortvalue", ref value, step: 0))
      {
        entry.BoxedValue = (short) value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawUShortEntry(ConfigEntry<ushort> entry)
    {
      var changed = false;

      var value = (int) entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<ushort> valueRange)
      {
        if (ImGui.SliderInt("##ushortvalue", ref value, valueRange.MinValue, valueRange.MaxValue))
        {
          entry.BoxedValue = (ushort) value;
          changed = true;
        }
      }
      else if (ImGui.InputInt("##ushortvalue", ref value, step: 0))
      {
        entry.BoxedValue = (ushort) value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawIntEntry(ConfigEntry<int> entry)
    {
      var changed = false;

      var value = entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<int> valueRange)
      {
        if (ImGui.SliderInt("##intvalue", ref value, valueRange.MinValue, valueRange.MaxValue))
        {
          entry.BoxedValue = value;
          changed = true;
        }
      }
      else if (ImGui.InputInt("##intvalue", ref value, step: 0))
      {
        entry.BoxedValue = value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawUIntEntry(ConfigEntry<uint> entry)
    {
      var changed = false;

      var value = (int) entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<uint> valueRange)
      {
        if (ImGui.SliderInt("##uintvalue", ref value, (int) valueRange.MinValue, (int) valueRange.MaxValue))
        {
          entry.BoxedValue = (uint) value;
          changed = true;
        }
      }
      else if (ImGui.InputInt("##uintvalue", ref value, step: 0))
      {
        entry.BoxedValue = (uint) value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawLongEntry(ConfigEntry<long> entry)
    {
      var changed = false;

      var value = (int) entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<long> valueRange)
      {
        if (ImGui.SliderInt("##longvalue", ref value, (int) valueRange.MinValue, (int) valueRange.MaxValue))
        {
          entry.BoxedValue = (long) value;
          changed = true;
        }
      }
      else if (ImGui.InputInt("##longvalue", ref value, step: 0))
      {
        entry.BoxedValue = (long) value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawULongEntry(ConfigEntry<ulong> entry)
    {
      var changed = false;

      var value = (int) entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<ulong> valueRange)
      {
        if (ImGui.SliderInt("##ulongvalue", ref value, (int) valueRange.MinValue, (int) valueRange.MaxValue))
        {
          entry.BoxedValue = (ulong) value;
          changed = true;
        }
      }
      else if (ImGui.InputInt("##ulongvalue", ref value, step: 0))
      {
        entry.BoxedValue = (ulong) value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawDefault(ConfigEntryBase entry)
    {
      var changed = false;

      var value = entry.BoxedValue;
      if (value != null)
        ImGuiHelper.TextDisabled($"{value}");
      else
        ImGuiHelper.TextDisabled("is null");

      return changed;
    }
  }
}