
using Assets.Scripts.Networking.Transports;
using Assets.Scripts.Serialization;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StationeersLaunchPad
{
  public class ModList
  {
    private readonly List<ModInfo> mods = new();

    public IEnumerable<ModInfo> AllMods => this.mods;
    public IEnumerable<ModInfo> EnabledMods => this.mods.Where(mod => mod.Enabled);
    public int IndexOf(ModInfo mod) => this.mods.IndexOf(mod);

    public void Clear() => this.mods.Clear();

    public void AddCore() => this.mods.Add(new ModInfo { Source = ModSource.Core });

    public void LoadLocalMods()
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
          this.mods.Add(new ModInfo()
          {
            Source = ModSource.Local,
            Wrapped = SteamTransport.ItemWrapper.WrapLocalItem(file, type),
          });
        }
      }
    }

    public async UniTask LoadWorkshopMods()
    {
      var items = await Steam.LoadWorkshopItems();

      foreach (var item in items)
      {
        this.mods.Add(new ModInfo
        {
          Source = ModSource.Workshop,
          Wrapped = SteamTransport.ItemWrapper.WrapWorkshopItem(item, "About\\About.xml"),
          WorkshopItem = item
        });
      }
    }

    public void LoadDetails()
    {
      foreach (var mod in this.mods)
        mod.LoadDetails();
    }

    public void LoadConfig()
    {
      var config = File.Exists(LaunchPadPaths.ConfigPath)
            ? XmlSerialization.Deserialize<ModConfig>(LaunchPadPaths.ConfigPath)
            : new ModConfig();
      config.CreateCoreMod();

      this.ApplyConfig(config);

      this.SaveConfig();
    }

    public void SaveConfig()
    {
      var config = this.MakeConfig();

      if (!config.SaveXml(LaunchPadPaths.ConfigPath))
        throw new Exception($"failed to save {WorkshopMenu.ConfigPath}");
    }

    private ModConfig MakeConfig()
    {
      var config = new ModConfig();
      foreach (var mod in mods)
      {
        config.Mods.Add(mod.Source switch
        {
          ModSource.Core => new CoreModData(),
          ModSource.Local => new LocalModData(mod.Path, mod.Enabled),
          ModSource.Workshop => new WorkshopModData(mod.Wrapped, mod.Enabled),
          _ => throw new InvalidOperationException($"invalid mod source: {mod.Source}"),
        });
      }
      return config;
    }

    private void ApplyConfig(ModConfig config)
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
        Logger.Global.LogDebug($"new mod added at {mod.Path}");
        mods[count++] = mod;
        mod.Enabled = true;
      }
    }

    // returns true if the mod was moved (even if it wasn't moved all the way to the target index)
    public bool MoveModTo(ModInfo mod, int index)
    {
      var curIndex = mods.IndexOf(mod);
      if (curIndex == -1)
        throw new InvalidOperationException($"unknown mod {mod.Source} {mod.DisplayName}");

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
        if (deps.Contains(this.mods[next]) && !shift(next))
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

    // returns true if all dependencies of enabled mods are satisfied
    public bool CheckDependencies()
    {
      var valid = true;
      foreach (var mod in this.mods)
      {
        if (!mod.Enabled)
          continue;
        if (mod.Source == ModSource.Core)
          continue;
        var missingDeps = false;
        foreach (var dep in mod.About.Dependencies ?? new())
        {
          if (this.mods.Any(mod2 => mod2 != mod && mod2.Enabled && mod2.Satisfies(dep)))
            continue;
          missingDeps = true;

          if (mod.DepsWarned)
            continue;

          Logger.Global.LogWarning($"{mod.Source} {mod.DisplayName} is missing dependency with workshop id {dep.Id}");

          var possible = this.mods.Where(mod2 => mod2 != mod && mod2.Satisfies(dep)).ToList();
          if (possible.Count == 0)
          {
            Logger.Global.LogWarning("No possible matches installed");
            continue;
          }

          Logger.Global.LogWarning("Possible matches:");
          foreach (var mod2 in possible)
            Logger.Global.LogWarning($"- {mod2.Source} {mod2.DisplayName}");
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
          if (firstSkipped != -1)
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

    private class OrderGraph
    {
      public static OrderGraph Build(List<ModInfo> mods)
      {
        var graph = new OrderGraph(mods);
        foreach (var mod in mods)
        {
          if (!mod.Enabled || mod.Source == ModSource.Core)
            continue;
          foreach (var before in mod.About.LoadBefore ?? new())
          {
            foreach (var mod2 in mods)
            {
              if (mod2 != mod && mod2.Enabled && mod2.Satisfies(before))
                graph.AddOrder(mod2, mod);
            }
          }
          foreach (var after in mod.About.LoadAfter ?? new())
          {
            foreach (var mod2 in mods)
            {
              if (mod2 != mod && mod2.Enabled && mod2.Satisfies(after))
                graph.AddOrder(mod, mod2);
            }
          }
        }
        return graph;
      }

      public readonly Dictionary<ModInfo, HashSet<ModInfo>> Befores = new();
      public readonly Dictionary<ModInfo, HashSet<ModInfo>> Afters = new();

      public bool HasCircular = false;

      private OrderGraph(List<ModInfo> mods)
      {
        foreach (var mod in mods)
        {
          this.Befores[mod] = new();
          this.Afters[mod] = new();
        }
      }

      public void AddOrder(ModInfo first, ModInfo second)
      {
        if (HasCircular)
          return;
        // if the second mod is already required before the first, this would add a circular reference
        if (this.Befores[first].Contains(second))
        {
          HasCircular = true;
          return;
        }

        // this order is already added
        if (this.Befores[second].Contains(first))
          return;

        this.Afters[first].Add(second);
        this.Befores[second].Add(first);
        foreach (var before in this.Befores[first])
          this.AddOrder(before, second);
        foreach (var after in this.Afters[second])
          this.AddOrder(first, after);
      }
    }
  }
}