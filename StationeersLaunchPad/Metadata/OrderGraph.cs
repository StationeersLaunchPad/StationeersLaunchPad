
using System.Collections.Generic;
using StationeersLaunchPad.Sources;

namespace StationeersLaunchPad.Metadata;

public class OrderGraph
{
  public static OrderGraph Build(List<ModInfo> mods)
  {
    var graph = new OrderGraph(mods);
    foreach (var mod in mods)
    {
      if (!mod.Enabled || mod.Source == ModSourceType.Core)
        continue;
      foreach (var modRef in mod.About.OrderBefore ?? [])
      {
        foreach (var mod2 in mods)
        {
          if (mod2 != mod && mod2.Enabled && mod2.Satisfies(modRef))
            graph.AddOrder(mod, mod2);
        }
      }
      foreach (var modRef in mod.About.OrderAfter ?? [])
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

  public readonly Dictionary<ModInfo, HashSet<ModInfo>> Befores = [];
  public readonly Dictionary<ModInfo, HashSet<ModInfo>> Afters = [];

  public bool HasCircular = false;

  private OrderGraph(List<ModInfo> mods)
  {
    foreach (var mod in mods)
    {
      Befores[mod] = [];
      Afters[mod] = [];
    }
  }

  public void AddOrder(ModInfo first, ModInfo second)
  {
    if (HasCircular)
      return;
    // if the second mod is already required before the first, this would add a circular reference
    if (Befores[first].Contains(second))
    {
      HasCircular = true;
      return;
    }

    // this order is already added
    if (Befores[second].Contains(first))
      return;

    Afters[first].Add(second);
    Befores[second].Add(first);
    foreach (var before in Befores[first])
      AddOrder(before, second);
    foreach (var after in Afters[second])
      AddOrder(first, after);
  }
}