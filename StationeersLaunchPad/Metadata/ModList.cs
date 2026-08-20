
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assets.Scripts.Networking.Transports;
using StationeersLaunchPad.Sources;

namespace StationeersLaunchPad.Metadata;

public class ModList
{
  private List<ModInfo> mods;

  public IEnumerable<ModInfo> AllMods => mods;
  public IEnumerable<ModInfo> EnabledMods => mods.Where(mod => mod.Enabled);
  public int IndexOf(ModInfo mod) => mods.IndexOf(mod);
  public bool IsBetaMod(ModInfo mod) =>
    mod.IsBetaProgramMod || mods.Any(stable => stable != mod && stable.IsBetaProgramFor(mod));

  public static ModList NewEmpty() => new();
  public static ModList FromDefs(List<ModDefinition> defs)
  {
    var mods = new List<ModInfo>();
    foreach (var def in defs)
      mods.Add(new(def));
    return new(mods);
  }

  private ModList() => mods = [];
  private ModList(List<ModInfo> mods) => this.mods = mods;

  public ModConfig ToModConfig()
  {
    var config = new ModConfig();
    foreach (var mod in mods)
      config.Mods.Add(mod.Def.ToModData(mod.Enabled));
    return config;
  }

  public void ApplyConfig(ModConfig config)
  {
    var modsByPath = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
    foreach (var mod in mods)
    {
      if (mod == null)
      {
        Logger.Global.LogWarning("Found Null mod in mods list.");
        continue;
      }

      //Speical case for core path
      if (mod.Source == ModSourceType.Core && string.IsNullOrEmpty(mod.DirectoryPath))
      {
        modsByPath["Core"] = mod;
        continue;
      }

      if (string.IsNullOrEmpty(mod.DirectoryPath))
      {
        Logger.Global.LogWarning($"Mod has empty path: {mod.GetType().Name}");
        continue;
      }
      var normalizedPath = NormalizePath(mod.DirectoryPath);
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

      var modPath = (string)modcfg.DirectoryPath;
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
      Logger.Global.LogDebug($"new mod added at {mod.DirectoryPath}");
      newMods.Add(mod);
      mod.Enabled = true;
    }
    mods = newMods;
  }

  public void ApplyProfile(ProfileData profile)
  {
    foreach (var mod in mods)
      mod.Enabled = false;

    var ordered = new List<ModInfo>();
    var matched = new HashSet<ModInfo>();
    var missing = 0;
    foreach (var entry in profile.Mods)
    {
      var mod = ProfileManager.FindMod(entry, mods);
      if (mod == null)
      {
        missing++;
        continue;
      }

      mod.Enabled = true;
      if (matched.Add(mod))
        ordered.Add(mod);
    }

    foreach (var mod in mods)
      if (!matched.Contains(mod))
        ordered.Add(mod);

    mods = ordered;
    if (missing > 0)
      Logger.Global.LogDebug($"Profile '{profile.Name}' skipped {missing} missing mod(s)");
  }

  // returns true if any mods were disabled
  public bool DisableDuplicates()
  {
    if (!Configs.DedupeMods.Value)
      return false;

    var localPrio = Configs.DedupePriorityLocal.Value;
    var workshopPrio = Configs.DedupePriorityWorkshop.Value;
    var repoPrio = Configs.DedupePriorityRepo.Value;

    var prefMods = new ModSet();
    var disabledMods = new List<ModInfo>();
    foreach (var mod in mods)
    {
      if (!mod.Enabled)
        continue;
      if (!prefMods.TryGetExisting(mod, out var pref) || !pref.Enabled)
      {
        prefMods.Add(mod);
        continue;
      }
      if (pref.IsBetaRelated(mod))
      {
        Logger.Global.LogDebug($"Beta program pair detected for {mod.Name}, skipping duplicate handling");
        prefMods.AddBetaRelated(mod);
        continue;
      }
      var nonPref = mod;
      var prefPrio = pref.Source switch
      {
        ModSourceType.Local => localPrio,
        ModSourceType.Workshop => workshopPrio,
        ModSourceType.Repo => repoPrio,
        _ => int.MinValue,
      };
      var nonprefPrio = nonPref.Source switch
      {
        ModSourceType.Local => localPrio,
        ModSourceType.Workshop => workshopPrio,
        ModSourceType.Repo => repoPrio,
        _ => int.MinValue,
      };
      // keep new mod if higher priority
      // if equal, we keep the existing mod as its earlier in the load order
      if (nonprefPrio > prefPrio)
        (pref, nonPref) = (nonPref, pref);
      prefMods.Remove(nonPref);
      prefMods.Add(pref);
      nonPref.Enabled = false;
      disabledMods.Add(nonPref);
    }

    foreach (var mod in disabledMods)
    {
      if (prefMods.TryGetExisting(mod, out var pref))
        Logger.Global.LogWarning($"{mod.Source} {mod.Name} disabled in favor of {pref.Source} {pref.Name}");
    }

    return disabledMods.Count > 0;
  }

  // returns true if all dependencies of enabled mods are satisfied
  public bool CheckDependencies()
  {
    var valid = true;
    foreach (var mod in mods)
    {
      if (!mod.Enabled)
        continue;
      if (mod.Source == ModSourceType.Core)
        continue;
      var missingDeps = false;
      foreach (var dep in mod.About.DependsOn ?? [])
      {
        if (!dep.IsValid)
          continue;
        if (mods.Any(mod2 => mod2 != mod && mod2.Enabled && mod2.Satisfies(dep)))
          continue;
        missingDeps = true;

        if (mod.DepsWarned)
          continue;

        Logger.Global.LogWarning($"{mod.Source} {mod.Name} is missing dependency {dep}");

        var possible = mods.Where(mod2 => mod2 != mod && mod2.Satisfies(dep)).ToList();
        if (possible.Count == 0)
        {
          Logger.Global.LogWarning("No possible matches installed");
          continue;
        }

        Logger.Global.LogWarning("Possible matches:");
        foreach (var mod2 in possible)
          Logger.Global.LogWarning($"- {mod2.Source} {mod2.Name}");
      }
      mod.DepsWarned = missingDeps;
      if (missingDeps)
        valid = false;
    }
    return valid;
  }

  // returns true if sort was successful
  public bool SortCanonical()
  {
    var graph = OrderGraph.Build(mods);
    if (graph.HasCircular)
    {
      SortCanonicalFallback();
      return false;
    }

    // Always start from the same base order. The previous implementation used
    // the current config/UI order as its tie-breaker, so otherwise-identical
    // clients and servers could produce different results.
    var enabled = mods
      .Where(mod => mod.Enabled && mod.Source != ModSourceType.Core)
      .OrderBy(mod => mod, CanonicalComparer.Instance)
      .ToList();
    var added = new HashSet<ModInfo>();
    var newOrder = new List<ModInfo>();

    var core = mods.FirstOrDefault(mod => mod.Source == ModSourceType.Core);
    if (core != null)
    {
      newOrder.Add(core);
      added.Add(core);
    }

    void addWithPrerequisites(ModInfo mod)
    {
      if (added.Contains(mod) || !mod.Enabled || mod.Source == ModSourceType.Core)
        return;

      foreach (var before in graph.Befores[mod]
        .Where(before => before.Enabled && before.Source != ModSourceType.Core)
        .OrderBy(before => before, ConstraintComparer.Instance))
        addWithPrerequisites(before);
      newOrder.Add(mod);
      added.Add(mod);
    }

    // Start with the end of each constraint chain so LoadBefore/LoadAfter
    // entries stay stacked around their target instead of being emitted early
    // merely because their name sorts first.
    foreach (var mod in enabled.Where(mod => !graph.Afters[mod]
      .Any(after => after.Enabled && after.Source != ModSourceType.Core)))
      addWithPrerequisites(mod);
    foreach (var mod in enabled)
      addWithPrerequisites(mod);

    newOrder.AddRange(mods
      .Where(mod => !mod.Enabled && mod.Source != ModSourceType.Core)
      .OrderBy(mod => mod, CanonicalComparer.Instance));

    if (newOrder.Count != mods.Count)
      throw new InvalidOperationException($"Sort did not add all mods: {newOrder.Count} != {mods.Count}");

    mods = newOrder;
    return true;
  }

  private void SortCanonicalFallback()
  {
    mods = [.. mods
      .OrderBy(mod => mod.Source == ModSourceType.Core ? 0 : mod.Enabled ? 1 : 2)
      .ThenBy(mod => mod, CanonicalComparer.Instance)];
  }

  private sealed class CanonicalComparer : IComparer<ModInfo>
  {
    public static readonly CanonicalComparer Instance = new();

    public int Compare(ModInfo x, ModInfo y)
    {
      if (ReferenceEquals(x, y))
        return 0;
      var result = string.Compare(x?.Name, y?.Name, StringComparison.OrdinalIgnoreCase);
      if (result != 0)
        return result;
      result = (x?.WorkshopHandle ?? 0).CompareTo(y?.WorkshopHandle ?? 0);
      if (result != 0)
        return result;
      result = string.Compare(x?.ModID, y?.ModID, StringComparison.OrdinalIgnoreCase);
      if (result != 0)
        return result;
      result = (x?.Source ?? default).CompareTo(y?.Source ?? default);
      if (result != 0)
        return result;
      result = string.Compare(
        x?.About?.Author, y?.About?.Author, StringComparison.OrdinalIgnoreCase);
      if (result != 0)
        return result;
      return string.Compare(
        Path.GetFileName(NormalizePath(x?.DirectoryPath).TrimEnd('/')),
        Path.GetFileName(NormalizePath(y?.DirectoryPath).TrimEnd('/')),
        StringComparison.OrdinalIgnoreCase);
    }
  }

  private sealed class ConstraintComparer : IComparer<ModInfo>
  {
    public static readonly ConstraintComparer Instance = new();

    public int Compare(ModInfo x, ModInfo y)
    {
      if (ReferenceEquals(x, y))
        return 0;
      if (x?.WorkshopHandle > 1 && y?.WorkshopHandle > 1)
      {
        var result = x.WorkshopHandle.CompareTo(y.WorkshopHandle);
        if (result != 0)
          return result;
      }
      return CanonicalComparer.Instance.Compare(x, y);
    }
  }

  private static string NormalizePath(string path) =>
    path?.Replace("\\", "/").Trim().ToLowerInvariant() ?? string.Empty;
}
