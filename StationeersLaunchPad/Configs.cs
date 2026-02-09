
using BepInEx.Configuration;
using StationeersLaunchPad.Loading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StationeersLaunchPad
{
  // config values that have different defaults depending on platform
  public struct ConfigDefaults
  {
    public bool CheckForUpdate;
    public bool AutoUpdateOnStart;
    public bool LinuxPathPatch;
  }

  public static class Configs
  {
    public static SortedConfigFile Sorted;

    public static ConfigEntry<bool> CheckForUpdate;
    public static ConfigEntry<bool> AutoUpdateOnStart;
    public static ConfigEntry<int> UpdateCheckTimeout;
    public static ConfigEntry<int> UpdateDownloadTimeout;
    public static ConfigEntry<bool> AutoLoadOnStart;
    public static ConfigEntry<bool> AutoSortOnStart;
    public static ConfigEntry<int> AutoLoadWaitTime;
    public static ConfigEntry<bool> DedupeMods;
    public static ConfigEntry<int> DedupePriorityLocal;
    public static ConfigEntry<int> DedupePriorityRepo;
    public static ConfigEntry<int> DedupePriorityWorkshop;
    public static ConfigEntry<LoadStrategyType> LoadStrategyType;
    public static ConfigEntry<LoadStrategyMode> LoadStrategyMode;
    public static ConfigEntry<bool> DisableSteamOnStart;
    public static ConfigEntry<string> SavePathOnStart;
    public static ConfigEntry<bool> PostUpdateCleanup;
    public static ConfigEntry<bool> OneTimeBoosterInstall;
    public static ConfigEntry<bool> AutoScrollLogs;
    public static ConfigEntry<LogSeverity> LogSeverities;
    public static ConfigEntry<bool> CompactLogs;
    public static ConfigEntry<bool> LinuxPathPatch;
    public static ConfigEntry<bool> CompactConfigPanel;
    public static ConfigEntry<int> RepoUpdateFrequency;
    public static ConfigEntry<int> RepoFetchTimeout;
    public static ConfigEntry<int> RepoModFetchTimeout;
    public static ConfigEntry<bool> RepoModValidateDigest;

    public static bool RunPostUpdateCleanup => CheckForUpdate.Value && PostUpdateCleanup.Value;
    public static bool RunOneTimeBoosterInstall => CheckForUpdate.Value && OneTimeBoosterInstall.Value;
    public static (LoadStrategyType, LoadStrategyMode) LoadStrategy => (LoadStrategyType.Value, LoadStrategyMode.Value);

    public static void Initialize(ConfigFile config)
    {
      var platformDefaults = Platform.ConfigDefaults;
      AutoLoadOnStart = config.Bind(
        new ConfigDefinition("Startup", "AutoLoadOnStart"),
        true,
        new ConfigDescription(
          "Automatically load after the configured wait time on startup. Can be stopped by clicking the loading window at the bottom"
        )
       );
      CheckForUpdate = config.Bind(
        new ConfigDefinition("Startup", "CheckForUpdate"),
        platformDefaults.CheckForUpdate,
        new ConfigDescription(
          "Automatically check for mod loader updates on startup."
        )
      );
      AutoUpdateOnStart = config.Bind(
        new ConfigDefinition("Startup", "AutoUpdateOnStart"),
        platformDefaults.AutoUpdateOnStart,
        new ConfigDescription(
          "Automatically update mod loader on startup. Ignored if CheckForUpdate is not also enabled."
        )
      );
      UpdateCheckTimeout = config.Bind(
        new ConfigDefinition("Startup", "UpdateCheckTimeout"),
        10,
        new ConfigDescription(
          "Timeout in seconds for fetching the latest StationeersLaunchPad version information.",
          new AcceptableValueRange<int>(5, 60)
        )
      );
      UpdateCheckTimeout = config.Bind(
        new ConfigDefinition("Startup", "UpdateDownloadTimeout"),
        45,
        new ConfigDescription(
          "Timeout in seconds for downloading an update to StationeersLaunchPad.",
          new AcceptableValueRange<int>(10, 300)
        )
      );
      AutoLoadWaitTime = config.Bind(
        new ConfigDefinition("Startup", "AutoLoadWaitTime"),
        3,
        new ConfigDescription(
          "How many seconds to wait before loading mods, then loading the game",
          new AcceptableValueRange<int>(0, 30)
        )
      );
      AutoSortOnStart = config.Bind(
        new ConfigDefinition("Startup", "AutoSort"),
        true,
        new ConfigDescription(
          "Automatically sort based on OrderBefore/OrderAfter tags in mod data"
        )
      );
      DisableSteamOnStart = config.Bind(
        new ConfigDefinition("Startup", "DisableSteam"),
        false,
        new ConfigDescription(
          "Don't attempt to load steam workshop mods"
        )
      );
      var oldDedupe = config.Bind(
        new ConfigDefinition("Mod Loading", "DisableDuplicates"), "");
      var dedupeDefault = oldDedupe.Value switch
      {
        "KeepLocal" => new int[] { 1, 2, 1, 3 },
        "KeepWorkshop" => new int[] { 1, 1, 2, 3 },
        _ => new int[] { 0, 2, 1, 3 },
      };
      config.Remove(oldDedupe.Definition);
      DedupeMods = config.Bind(
        new ConfigDefinition("Mod Dedupe", "DedupeMods"),
        dedupeDefault[0] != 0,
        new ConfigDescription(
          "Automatically disable duplicate mods, keeping the mod type with the highest priority"
        )
      );
      DedupePriorityLocal = config.Bind(
        new ConfigDefinition("Mod Dedupe", "DedupePriorityLocal"),
        dedupeDefault[1],
        new ConfigDescription(
          "Priority of Local mods when deduping, lower priority gets disabled"
        )
      );
      DedupePriorityWorkshop = config.Bind(
        new ConfigDefinition("Mod Dedupe", "DedupePriorityWorkshop"),
        dedupeDefault[2],
        new ConfigDescription(
          "Priority of Workshop mods when deduping, lower priority gets disabled"
        )
      );
      DedupePriorityRepo = config.Bind(
        new ConfigDefinition("Mod Dedupe", "DedupePriorityRepo"),
        dedupeDefault[3],
        new ConfigDescription(
          "Priority of Repo mods when deduping, lower priority gets disabled"
        )
      );
      LoadStrategyType = config.Bind(
        new ConfigDefinition("Mod Loading", "LoadStrategyType"),
        Loading.LoadStrategyType.Linear,
        new ConfigDescription(
          "Linear type loads mods one by one in sequential order. More types of mod loading will be added later."
        )
      );
      LoadStrategyMode = config.Bind(
        new ConfigDefinition("Mod Loading", "LoadStrategyMode"),
        Loading.LoadStrategyMode.Serial,
        new ConfigDescription(
          "Parallel mode loads faster for a large number of mods, but may fail in extremely rare cases. Switch to serial mode if running into loading issues."
        )
      );
      SavePathOnStart = config.Bind(
        new ConfigDefinition("Mod Loading", "SavePathOverride"),
        "",
        new ConfigDescription(
          "This setting allows you to override the default path that config and save files are stored. Notice, due to how this path is implemented in the base game, this setting can only be applied on server start.  Changing it while in game will not have an effect until after a restart."
        )
      );
      RepoUpdateFrequency = config.Bind(
        new ConfigDefinition("Mod Repos", "RepoUpdateFrequency"),
        300,
        new ConfigDescription(
          "Minimum time in seconds before checking a mod repo for new versions."
        )
      );
      RepoFetchTimeout = config.Bind(
        new ConfigDefinition("Mod Repos", "RepoFetchTimeout"),
        15,
        new ConfigDescription(
          "Maximum time in seconds to wait for listing available versions in a mod repo."
        )
      );
      RepoModFetchTimeout = config.Bind(
        new ConfigDefinition("Mod Repos", "RepoModFetchTimeout"),
        60,
        new ConfigDescription(
          "Maximum time in seconds to wait for downloading a new mod version."
        )
      );
      RepoModValidateDigest = config.Bind(
        new ConfigDefinition("Mod Repos", "RepoModValidateDigest"),
        true,
        new ConfigDescription(
          "Reject new mod versions when they don't match the digest provided by the repo."
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
      LinuxPathPatch = config.Bind(
        new ConfigDefinition("Internal", "LinuxPathPatch"),
        platformDefaults.LinuxPathPatch,
        new ConfigDescription(
          "Patch xml mod data loading to properly handle linux path separators"
        )
      );
      CompactConfigPanel = config.Bind(
        new ConfigDefinition("UI", "CompactConfigPanel"),
        false,
        new ConfigDescription(
          "Display configuration entires on the same line with their names"
        )
      );
      Sorted = new SortedConfigFile(config);
    }
  }

  public class SortedConfigFile
  {
    public readonly ConfigFile ConfigFile;
    public readonly string FileName;
    public readonly List<SortedConfigCategory> Categories;

    public SortedConfigFile(ConfigFile configFile)
    {
      this.ConfigFile = configFile;
      this.FileName = Path.GetFileName(configFile.ConfigFilePath);
      var categories = new List<SortedConfigCategory>();
      foreach (var group in configFile.Select(entry => entry.Value).GroupBy(entry => entry.Definition.Section))
      {
        categories.Add(new SortedConfigCategory(
          configFile,
          group.Key,
          group
        ));
      }
      categories.Sort((a, b) => a.Category.CompareTo(b.Category));
      this.Categories = categories;
    }
  }

  public class SortedConfigCategory
  {
    public readonly ConfigFile ConfigFile;
    public readonly string Category;
    public readonly List<ConfigEntryWrapper> Entries;

    public SortedConfigCategory(ConfigFile configFile, string category, IEnumerable<ConfigEntryBase> entries)
    {
      this.ConfigFile = configFile;
      this.Category = category;
      this.Entries = entries.Select(entry => new ConfigEntryWrapper(entry)).ToList();
      this.Entries.Sort((a, b) =>
      {
        var order = a.Order.CompareTo(b.Order);
        return order != 0 ? order : a.Entry.Definition.Key.CompareTo(b.Entry.Definition.Key);
      });
    }
  }

  public class ConfigEntryWrapper
  {
    public readonly ConfigEntryBase Entry;
    public int Order = 0;
    public bool RequireRestart = false;
    public bool Disabled = false;
    public bool Visible = true;
    public string DisplayName;
    public string Format = "%.3f";
    public Func<ConfigEntryBase, bool> CustomDrawer;
    public ConfigDefinition Definition => this.Entry.Definition;
    public ConfigDescription Description => this.Entry.Description;
    public object BoxedValue => this.Entry.BoxedValue;

    public ConfigEntryWrapper(ConfigEntryBase entry)
    {
      this.DisplayName = entry.Definition.Key;
      this.Entry = entry;
      foreach (var tag in entry.Description.Tags)
      {
        switch (tag)
        {
          case KeyValuePair<string, int> { Key: "Order", Value: var order }:
            this.Order = order;
            break;
          case KeyValuePair<string, bool> { Key: "RequireRestart", Value: var requireRestart }:
            this.RequireRestart = requireRestart;
            break;
          case KeyValuePair<string, bool> { Key: "Disabled", Value: var disabled }:
            this.Disabled = disabled;
            break;
          case KeyValuePair<string, bool> { Key: "Visible", Value: var visible }:
            this.Visible = visible;
            break;
          case KeyValuePair<string, string> { Key: "DisplayName", Value: var displayName }:
            this.DisplayName = displayName;
            break;
          case KeyValuePair<string, string> { Key: "Format", Value: var format }:
            this.Format = format;
            break;
          case KeyValuePair<string, Func<ConfigEntryBase, bool>> { Key: "CustomDrawer", Value: var customDrawer }:
            this.CustomDrawer = customDrawer;
            break;
        }
      }
    }
  }
}