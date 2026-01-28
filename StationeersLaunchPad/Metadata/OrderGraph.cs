
using StationeersLaunchPad.Sources;
using System.Collections.Generic;

namespace StationeersLaunchPad.Metadata
{
  public class OrderGraph
  {
    public static OrderGraph Build(List<ModInfo> mods)
    {
      var graph = new OrderGraph(mods);
      foreach (var mod in mods)
      {
        if (!mod.Enabled || mod.Source == ModSourceType.Core)
          continue;
        foreach (var modRef in mod.About.OrderBefore ?? new())
        {
          foreach (var mod2 in mods)
          {
            if (mod2 != mod && mod2.Enabled && mod2.Satisfies(modRef))
              graph.AddOrder(mod, mod2);
          }
        }
        foreach (var modRef in mod.About.OrderAfter ?? new())
        {
          foreach (var mod2 in mods)
          {
            if (mod2 != mod && mod2.Enabled && mod2.Satisfies(modRef))
              graph.AddOrder(mod2, mod);
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