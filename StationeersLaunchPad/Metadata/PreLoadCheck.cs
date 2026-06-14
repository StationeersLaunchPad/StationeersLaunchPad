using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Assets.Scripts.Serialization;
using StationeersLaunchPad.Loading;
using StationeersLaunchPad.Sources;
using Steamworks;

namespace StationeersLaunchPad.Metadata;

public enum CheckSeverity { Info, Warning, Error }

public class CheckIssue
{
  public CheckSeverity Severity;
  public string Category;
  public string Message;
  public ModInfo Mod;
}

public class CheckResult
{
  public readonly List<CheckIssue> Issues = [];
  public int Errors => Issues.Count(i => i.Severity == CheckSeverity.Error);
  public int Warnings => Issues.Count(i => i.Severity == CheckSeverity.Warning);
  public int Infos => Issues.Count(i => i.Severity == CheckSeverity.Info);
  public bool Ok => Errors == 0 && Warnings == 0;

  public void Add(CheckSeverity severity, string category, string message, ModInfo mod = null) =>
    Issues.Add(new CheckIssue { Severity = severity, Category = category, Message = message, Mod = mod });
}

// Runs static, pre-load health checks over the configured mod list and surfaces the result
// via Current. Cannot detect runtime incompatibilities (Harmony/API mismatches) - those only
// appear when a mod is loaded - but remembers mods that failed loading in a previous session.
public static class PreLoadCheck
{
  // A Workshop mod not updated in this long is flagged as "possibly outdated" (heuristic only).
  private const int StaleMonths = 12;

  public static CheckResult Current { get; private set; }

  public static void Run(ModList modList)
  {
    var result = new CheckResult();
    var all = modList.AllMods.ToList();
    var enabled = all.Where(m => m.Enabled).ToList();

    CheckCompatibility(enabled, result);
    CheckDependencies(enabled, all, result);
    CheckDuplicates(enabled, result);
    CheckOrder(all, result);
    CheckMetadata(enabled, result);
    CheckWorkshop(enabled, result);
    CheckPastFailures(enabled, result);

    Current = result;
  }

  private static void CheckCompatibility(List<ModInfo> enabled, CheckResult result)
  {
    if (!ModCompatScanner.HasScanned)
      return;
    foreach (var mod in enabled)
    {
      if (mod.Source == ModSourceType.Core)
        continue;
      var missing = ModCompatScanner.GetMissing(mod);
      if (missing.Count == 0)
        continue;
      var sample = string.Join(", ", missing.Take(3));
      if (missing.Count > 3)
        sample += $", +{missing.Count - 3} more";
      result.Add(CheckSeverity.Error, "Incompatible",
        $"{mod.Name} is incompatible with this game version (missing: {sample})", mod);
    }
  }

  // Persists which mods failed to load this session so the next launch can warn about them.
  public static void SaveLoadResults()
  {
    try
    {
      var failed = ModLoader.LoadedMods.Where(m => m.LoadFailed).Select(m => m.Info);
      FailedModsStore.Save(failed);
    }
    catch (Exception ex)
    {
      Logger.Global.LogException(ex);
    }
  }

  private static void CheckDependencies(List<ModInfo> enabled, List<ModInfo> all, CheckResult result)
  {
    foreach (var mod in enabled)
    {
      if (mod.Source == ModSourceType.Core)
        continue;
      foreach (var dep in mod.About?.DependsOn ?? [])
      {
        if (!dep.IsValid)
          continue;
        if (enabled.Any(m => m != mod && m.Satisfies(dep)))
          continue;
        if (all.Any(m => m != mod && m.Satisfies(dep)))
          result.Add(CheckSeverity.Warning, "Disabled dependency",
            $"{mod.Name} needs {dep}, which is installed but disabled", mod);
        else
          result.Add(CheckSeverity.Error, "Missing dependency",
            $"{mod.Name} needs {dep}, which is not installed", mod);
      }
    }
  }

  private static void CheckDuplicates(List<ModInfo> enabled, CheckResult result)
  {
    foreach (var group in enabled
      .Where(m => m.Source != ModSourceType.Core && m.WorkshopHandle > 1)
      .GroupBy(m => m.WorkshopHandle)
      .Where(g => g.Count() > 1))
    {
      var names = string.Join(", ", group.Select(m => $"{m.Source}"));
      result.Add(CheckSeverity.Warning, "Duplicate mod",
        $"{group.First().Name} is enabled from multiple sources ({names})", group.First());
    }

    foreach (var group in enabled
      .Where(m => m.Source != ModSourceType.Core && !string.IsNullOrEmpty(m.ModID))
      .GroupBy(m => m.ModID)
      .Where(g => g.Count() > 1 && g.Select(m => m.WorkshopHandle).Distinct().Count() > 1))
    {
      result.Add(CheckSeverity.Warning, "Duplicate mod",
        $"ModID '{group.Key}' is provided by {group.Count()} enabled mods", group.First());
    }
  }

  private static void CheckOrder(List<ModInfo> all, CheckResult result)
  {
    if (OrderGraph.Build(all).HasCircular)
      result.Add(CheckSeverity.Error, "Load order",
        "Circular load order detected between mods (OrderBefore/OrderAfter)");
  }

  private static void CheckMetadata(List<ModInfo> enabled, CheckResult result)
  {
    foreach (var mod in enabled)
      if (mod.Name?.StartsWith("[Invalid About.xml]") ?? false)
        result.Add(CheckSeverity.Warning, "Invalid metadata",
          $"{mod.DirectoryName} has an invalid or missing About.xml", mod);
  }

  private static void CheckWorkshop(List<ModInfo> enabled, CheckResult result)
  {
    var staleCutoff = DateTime.Now.AddMonths(-StaleMonths);

    foreach (var mod in enabled)
    {
      if (mod.Def is not WorkshopModDefinition wdef)
        continue;
      var item = wdef.Item;

      if (item.Result == Result.FileNotFound)
        result.Add(CheckSeverity.Error, "Workshop",
          $"{mod.Name} is no longer available on the Workshop", mod);
      else if (string.IsNullOrEmpty(item.Directory) || !Directory.Exists(item.Directory))
        result.Add(CheckSeverity.Error, "Workshop",
          $"{mod.Name} is not downloaded (files missing)", mod);
      else if (item.NeedsUpdate)
        result.Add(CheckSeverity.Warning, "Workshop",
          $"{mod.Name} has a Workshop update available", mod);

      if (mod.Updated is { } updated && updated < staleCutoff)
        result.Add(CheckSeverity.Info, "Possibly outdated",
          $"{mod.Name} hasn't been updated since {updated:yyyy-MM-dd} (heuristic)", mod);
    }
  }

  private static void CheckPastFailures(List<ModInfo> enabled, CheckResult result)
  {
    var record = FailedModsStore.Load();
    if (record.Mods.Count == 0)
      return;
    foreach (var mod in enabled)
      if (mod.Source != ModSourceType.Core && FailedModsStore.Matches(record, mod))
        result.Add(CheckSeverity.Warning, "Previous failure",
          $"{mod.Name} failed to load in a previous session", mod);
  }
}

[XmlRoot("FailedMods")]
public class FailedModsRecord
{
  [XmlElement("Mod")]
  public List<FailedModEntry> Mods = [];
}

public class FailedModEntry
{
  [XmlAttribute("WorkshopHandle")]
  public ulong WorkshopHandle;
  [XmlAttribute("ModID")]
  public string ModID;
  [XmlAttribute("Name")]
  public string Name;
}

public static class FailedModsStore
{
  private static string Path => System.IO.Path.Join(LaunchPadPaths.SavePath, "failed_mods.xml");

  public static FailedModsRecord Load()
  {
    try
    {
      if (File.Exists(Path))
        return XmlSerialization.Deserialize<FailedModsRecord>(Path) ?? new();
    }
    catch (Exception ex)
    {
      Logger.Global.LogWarning($"failed to read {Path}: {ex.Message}");
    }
    return new();
  }

  public static void Save(IEnumerable<ModInfo> failed)
  {
    var record = new FailedModsRecord();
    foreach (var mod in failed)
      record.Mods.Add(new FailedModEntry
      {
        WorkshopHandle = mod.WorkshopHandle,
        ModID = mod.ModID,
        Name = mod.Name,
      });
    if (!record.SaveXml(Path))
      Logger.Global.LogWarning($"failed to save {Path}");
  }

  public static bool Matches(FailedModsRecord record, ModInfo mod) =>
    record.Mods.Any(e =>
      (e.WorkshopHandle > 1 && e.WorkshopHandle == mod.WorkshopHandle)
      || (!string.IsNullOrEmpty(e.ModID) && e.ModID == mod.ModID)
      || (!string.IsNullOrEmpty(e.Name) && e.Name == mod.Name));
}
