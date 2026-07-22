
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using StationeersLaunchPad.Loading;

namespace StationeersLaunchPad;

public enum UiAccentColor
{
  Classic,
  Orange,
  Blue,
  Green,
}

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
  public static ConfigEntry<bool> RetainWorkshopMods;
  public static ConfigEntry<bool> PostUpdateCleanup;
  public static ConfigEntry<bool> OneTimeBoosterInstall;
  public static ConfigEntry<bool> AutoScrollLogs;
  public static ConfigEntry<LogSeverity> LogSeverities;
  public static ConfigEntryWrapper LogSeveritiesWrapper;
  public static ConfigEntry<bool> CompactLogs;
  public static ConfigEntry<bool> LinuxPathPatch;
  public static ConfigEntry<bool> CompactConfigPanel;
  public static ConfigEntry<UiAccentColor> UiAccent;
  public static ConfigEntry<bool> RepoCheckUpdates;
  public static ConfigEntry<int> RepoUpdateFrequency;
  public static ConfigEntry<int> RepoFetchTimeout;
  public static ConfigEntry<bool> RepoModCheckUpdates;
  public static ConfigEntry<int> RepoModFetchTimeout;
  public static ConfigEntry<bool> RepoModValidateDigest;
  public static ConfigEntry<bool> RepoModValidateVersion;
  public static ConfigEntry<string> ModProfile;

  public static ConfigEntry<bool> NewsCheckOnStart;
  public static ConfigEntry<string> NewsFeedUrl;

  public const string DefaultNewsFeedUrl = "https://raw.githubusercontent.com/StationeersLaunchPad/news/main/news.xml";
  public static ConfigEntry<int> NewsFetchTimeout;
  public static ConfigEntry<string> NewsDismissedIds;

    // Note: we use EffectiveNewsFeedUrl so the default value is never written to the config file.
    // This makes it possible to change this default in future versions.
  public static string EffectiveNewsFeedUrl =>
      string.IsNullOrWhiteSpace(NewsFeedUrl.Value) ? DefaultNewsFeedUrl : NewsFeedUrl.Value;

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
    UpdateDownloadTimeout = config.Bind(
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
        "Automatically sort based on dependencies and OrderBefore/OrderAfter tags in mod data"
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
    int[] dedupeDefault = oldDedupe.Value switch
    {
      "KeepLocal" => [1, 2, 1, 3],
      "KeepWorkshop" => [1, 1, 2, 3],
      _ => [0, 2, 1, 3],
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
    RetainWorkshopMods = config.Bind(
      new ConfigDefinition("Mod Loading", "RetainWorkshopMods"),
      true,
      new ConfigDescription(
        "When running with steam disabled, use the workshop mods already in the mod config as the list of available workshop mods."
      )
    );
    RepoCheckUpdates = config.Bind(
      new ConfigDefinition("Mod Repos", "RepoCheckUpdates"),
      true,
      new ConfigDescription(
        "Check mod repos for new available mod versions on startup"
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
    RepoModCheckUpdates = config.Bind(
      new ConfigDefinition("Mod Repos", "RepoModCheckUpdates"),
      true,
      new ConfigDescription(
        "Check configured repo mods for new versions on startup."
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
    RepoModValidateVersion = config.Bind(
      new ConfigDefinition("Mod Repos", "RepoModValidateVersion"),
      true,
      new ConfigDescription(
        "Reject new mod versions when they don't match the target ModID and Version."
      )
    );
    NewsCheckOnStart = config.Bind(
      new ConfigDefinition("News", "NewsCheckOnStart"),
      true,
      new ConfigDescription(
        "Check for news notices on startup."
      )
    );
    NewsFeedUrl = config.Bind(
      new ConfigDefinition("News", "NewsFeedUrl"),
      "",
      new ConfigDescription(
        "URL to fetch the news feed from. Leave empty to use the built-in default. Set a value to override."
      )
    );
    NewsFetchTimeout = config.Bind(
      new ConfigDefinition("News", "NewsFetchTimeout"),
      10,
      new ConfigDescription(
        "Timeout in seconds for fetching news notices.",
        new AcceptableValueRange<int>(5, 60)
      )
    );
    NewsDismissedIds = config.Bind(
      new ConfigDefinition("News", "NewsDismissedIds"),
      "",
      new ConfigDescription(
        "Comma-separated list of dismissed news notice IDs. Automatically managed."
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
    LogSeveritiesWrapper = new(LogSeverities);
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
    ModProfile = config.Bind(
      new ConfigDefinition("Internal", "ModProfile"),
      "",
      new ConfigDescription(
        "The active mod profile. Leave empty to use the normal mod configuration."
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
    UiAccent = config.Bind(
      new ConfigDefinition("Appearance", "AccentColor"),
      UiAccentColor.Classic,
      new ConfigDescription(
        "Accent color for LaunchPad controls. Classic uses the existing SLP theme."
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
    ConfigFile = configFile;
    FileName = Path.GetFileName(configFile.ConfigFilePath);
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
    Categories = categories;
  }
}

public class SortedConfigCategory
{
  public readonly ConfigFile ConfigFile;
  public readonly string Category;
  public readonly List<ConfigEntryWrapper> Entries;

  public SortedConfigCategory(ConfigFile configFile, string category, IEnumerable<ConfigEntryBase> entries)
  {
    ConfigFile = configFile;
    Category = category;
    Entries = [.. entries.Select(entry => new ConfigEntryWrapper(entry))];
    Entries.Sort((a, b) =>
    {
      var order = a.Order.CompareTo(b.Order);
      return order != 0 ? order : a.Entry.Definition.Key.CompareTo(b.Entry.Definition.Key);
    });
  }
}

public class ConfigEntryWrapper
{
  public readonly ConfigEntryBase Entry;
  public readonly int Order = 0;
  public readonly bool RequireRestart = false;
  public readonly bool Disabled = false;
  public readonly bool Visible = true;
  public readonly string DisplayName;
  public readonly string Format;
  public readonly Func<ConfigEntryBase, bool> CustomDrawer;
  public ConfigDefinition Definition => Entry.Definition;
  public ConfigDescription Description => Entry.Description;

  public ConfigEntryWrapper(ConfigEntryBase entry)
  {
    DisplayName = entry.Definition.Key;
    Entry = entry;
    foreach (var tag in entry.Description.Tags)
    {
      switch (tag)
      {
        case KeyValuePair<string, int> { Key: "Order", Value: var order }:
          Order = order;
          break;
        case KeyValuePair<string, bool> { Key: "RequireRestart", Value: var requireRestart }:
          RequireRestart = requireRestart;
          break;
        case KeyValuePair<string, bool> { Key: "Disabled", Value: var disabled }:
          Disabled = disabled;
          break;
        case KeyValuePair<string, bool> { Key: "Visible", Value: var visible }:
          Visible = visible;
          break;
        case KeyValuePair<string, string> { Key: "DisplayName", Value: var displayName }:
          DisplayName = displayName;
          break;
        case KeyValuePair<string, string> { Key: "Format", Value: var format }:
          Format = format;
          break;
        case KeyValuePair<string, Func<ConfigEntryBase, bool>> { Key: "CustomDrawer", Value: var customDrawer }:
          CustomDrawer = customDrawer;
          break;
      }
    }
  }
}
