
using Assets.Scripts.Networking.Transports;
using StationeersLaunchPad.Sources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StationeersLaunchPad.Metadata
{
  public class ModList
  {
    private readonly List<ModInfo> mods;

    public IEnumerable<ModInfo> AllMods => this.mods;
    public IEnumerable<ModInfo> EnabledMods => this.mods.Where(mod => mod.Enabled);
    public int IndexOf(ModInfo mod) => this.mods.IndexOf(mod);

    public static ModList NewEmpty() => new();
    public static ModList FromDefs(List<ModDefinition> defs)
    {
      var mods = new List<ModInfo>();
      foreach (var def in defs)
        mods.Add(new(def));
      return new(mods);
    }

    private ModList() => this.mods = new();
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
      var count = 0;

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
            mods[count++] = coreMod;
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
          mods[count++] = mod;
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
        mods[count++] = mod;
        mod.Enabled = true;
      }
    }

    // returns true if the mod was moved (even if it wasn't moved all the way to the target index)
    public bool MoveModTo(ModInfo mod, int index, bool keepOrder)
    {
      var curIndex = mods.IndexOf(mod);
      if (curIndex == -1)
        throw new InvalidOperationException($"unknown mod {mod.Source} {mod.Name}");

      if (curIndex == index)
        return false;

      var graph = OrderGraph.Build(this.mods);

      var dir = index > curIndex ? 1 : -1;
      var deps = index > curIndex ? graph.Afters[mod] : graph.Befores[mod];

      bool shift(int idx)
      {
        var next = idx + dir;
        if (next < 0 || next >= this.mods.Count)
          return false;
        if (keepOrder && deps.Contains(this.mods[next]) && !shift(next))
          return false;
        (this.mods[idx], this.mods[next]) = (this.mods[next], this.mods[idx]);
        return true;
      }

      var anyMove = false;
      while (curIndex != index)
      {
        if (!shift(curIndex))
          break;
        anyMove = true;
        curIndex += dir;
      }
      return anyMove;
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
      foreach (var mod in this.mods)
      {
        if (!mod.Enabled)
          continue;
        if (!prefMods.TryGetExisting(mod, out var pref) || !pref.Enabled)
        {
          prefMods.Add(mod);
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
      foreach (var mod in this.mods)
      {
        if (!mod.Enabled)
          continue;
        if (mod.Source == ModSourceType.Core)
          continue;
        var missingDeps = false;
        foreach (var dep in mod.About.DependsOn ?? new())
        {
          if (!dep.IsValid)
            continue;
          if (this.mods.Any(mod2 => mod2 != mod && mod2.Enabled && mod2.Satisfies(dep)))
            continue;
          missingDeps = true;

          if (mod.DepsWarned)
            continue;

          Logger.Global.LogWarning($"{mod.Source} {mod.Name} is missing dependency {dep}");

          var possible = this.mods.Where(mod2 => mod2 != mod && mod2.Satisfies(dep)).ToList();
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
    public bool SortByDeps()
    {
      var graph = OrderGraph.Build(this.mods);
      if (graph.HasCircular)
        return false;

      // loop through each mod, adding any who are disabled or have all dependencies met.
      // if a mod is skipped, we move back to it as soon as another mod is added to limit how far mods get pushed forward
      // this makes this an n^2 sort worst case. while we could likely do better on this complexity, this approach is simple and
      // has a negligible runtime in up to hundreds of mods.
      var added = new HashSet<ModInfo>();
      var newOrder = new List<ModInfo>();
      bool areDepsAdded(ModInfo mod)
      {
        foreach (var mod2 in graph.Befores[mod])
          if (!added.Contains(mod2))
            return false;
        return true;
      }

      var idx = 0;
      var firstSkipped = -1;

      while (idx < this.mods.Count)
      {
        var mod = this.mods[idx];
        if (added.Contains(mod))
        {
          idx++;
          continue;
        }
        if (mod.Enabled && !areDepsAdded(mod))
        {
          if (firstSkipped == -1)
            firstSkipped = idx;
          idx++;
          continue;
        }

        newOrder.Add(mod);
        added.Add(mod);
        if (firstSkipped != -1)
        {
          idx = firstSkipped;
          firstSkipped = -1;
        }
        else
          idx++;
      }

      if (newOrder.Count != this.mods.Count)
        throw new InvalidOperationException($"Sort did not add all mods: {newOrder.Count} != {this.mods.Count}");

      for (var i = 0; i < newOrder.Count; i++)
        this.mods[i] = newOrder[i];

      return true;
    }

    private static string NormalizePath(string path) =>
      path?.Replace("\\", "/").Trim().ToLowerInvariant() ?? string.Empty;
  }
}