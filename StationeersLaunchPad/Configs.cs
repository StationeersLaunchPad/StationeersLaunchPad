
using Assets.Scripts;
using BepInEx.Configuration;
using StationeersLaunchPad.Loading;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StationeersLaunchPad
{
  public enum DisableDuplicateMode
  {
    None, KeepLocal, KeepWorkshop
  }

  public static class Configs
  {
    public static SortedConfigFile Sorted;

    public static ConfigEntry<bool> CheckForUpdate;
    public static ConfigEntry<bool> AutoUpdateOnStart;
    public static ConfigEntry<bool> AutoLoadOnStart;
    public static ConfigEntry<bool> AutoSortOnStart;
    public static ConfigEntry<int> AutoLoadWaitTime;
    public static ConfigEntry<DisableDuplicateMode> DisableDuplicates;
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

    public static bool RunPostUpdateCleanup => CheckForUpdate.Value && PostUpdateCleanup.Value;
    public static bool RunOneTimeBoosterInstall => CheckForUpdate.Value && OneTimeBoosterInstall.Value;
    public static (LoadStrategyType, LoadStrategyMode) LoadStrategy => (LoadStrategyType.Value, LoadStrategyMode.Value);

    public static void Initialize(ConfigFile config)
    {
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
      DisableSteamOnStart = config.Bind(
        new ConfigDefinition("Startup", "DisableSteam"),
        false,
        new ConfigDescription(
          "Don't attempt to load steam workshop mods"
        )
      );
      DisableDuplicates = config.Bind(
        new ConfigDefinition("Mod Loading", "DisableDuplicates"),
        DisableDuplicateMode.None,
        new ConfigDescription(
          "Automatically disable duplicate mods, keeping the mod with the preferred source"
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
      LinuxPathPatch = config.Bind(
        new ConfigDefinition("Internal", "LinuxPathPatch"),
        Path.DirectorySeparatorChar != '\\',
        new ConfigDescription(
          "Patch xml mod data loading to properly handle linux path separators"
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
          group.OrderBy(entry => entry.Definition.Key).ToList()
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
    public readonly List<ConfigEntryBase> Entries;

    public SortedConfigCategory(ConfigFile configFile, string category, List<ConfigEntryBase> entries)
    {
      this.ConfigFile = configFile;
      this.Category = category;
      this.Entries = entries;
    }
  }
}