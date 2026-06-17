using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;

namespace StationeersLaunchPad.UI;

// Renders the enabled mods as a dependency tree: "library" mods at the top with the
// mods that depend on them nested underneath. Surfaces missing/disabled dependencies and
// lets the user enable disabled ones in one click. Returns whether the mod list changed
// and which mod (if any) the user clicked to inspect.
public static class ModTreePanel
{
  private class DepInfo
  {
    public readonly List<ModInfo> DependsOn = [];      // enabled mods this one requires
    public readonly List<ModInfo> Dependents = [];     // enabled mods that require this one
    public readonly List<ModReference> Missing = [];   // declared deps that aren't installed
    public readonly List<ModReference> Disabled = [];  // declared deps installed but disabled
  }

  public static (bool changed, ModInfo selected) Draw(ModList modList, string search)
  {
    var all = modList.AllMods.ToList();
    var enabled = all.Where(m => m.Enabled).ToList();
    var info = Resolve(enabled, all);

    // When searching, keep matching mods plus the dependency chain above them.
    var visible = ComputeVisible(enabled, info, search);

    var changed = DrawLegend(modList, enabled, all, info);
    ImGui.Separator();

    ModInfo selected = null;
    var counter = 0;

    // Core first (the game's own assemblies/data).
    var core = enabled.FirstOrDefault(m => m.Source == ModSourceType.Core);
    if (core != null && visible.Contains(core))
      DrawNode(core, info, visible, [], ref counter, ref selected);

    // Dependency roots: enabled mods that require nothing but are required by others.
    var roots = enabled
      .Where(m => m.Source != ModSourceType.Core
        && info[m].DependsOn.Count == 0
        && info[m].Dependents.Count > 0
        && visible.Contains(m))
      .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase);
    foreach (var mod in roots)
      DrawNode(mod, info, visible, [], ref counter, ref selected);

    // Standalone mods: no dependencies and nothing depends on them.
    var standalone = enabled
      .Where(m => m.Source != ModSourceType.Core
        && info[m].DependsOn.Count == 0
        && info[m].Dependents.Count == 0
        && visible.Contains(m))
      .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
      .ToList();

    if (standalone.Count > 0)
    {
      ImGui.Separator();
      if (ImGui.TreeNodeEx($"Standalone ({standalone.Count}) - no dependencies",
          ImGuiTreeNodeFlags.SpanAvailWidth))
      {
        foreach (var mod in standalone)
        {
          ImGui.PushID(counter++);
          DrawLeafRow(mod, ref selected);
          ImGui.PopID();
        }
        ImGui.TreePop();
      }
    }

    return (changed, selected);
  }

  private static bool DrawLegend(ModList modList, List<ModInfo> enabled,
    List<ModInfo> all, Dictionary<ModInfo, DepInfo> info)
  {
    var total = enabled.Count(m => m.Source != ModSourceType.Core);
    var withDeps = enabled.Count(m => info[m].DependsOn.Count > 0);
    var missing = enabled.Count(m => info[m].Missing.Count > 0);

    ImGuiHelper.TextDisabled($"{total} enabled mods - {withDeps} with dependencies");

    if (missing > 0)
      ImGuiHelper.TextError($"{missing} mod(s) have missing dependencies (not installed)");

    // Collect the distinct installed-but-disabled mods that an enabled mod requires.
    var disabledProviders = new HashSet<ModInfo>();
    foreach (var mod in enabled)
      foreach (var modRef in info[mod].Disabled)
        foreach (var provider in all.Where(m => !m.Enabled && m.Satisfies(modRef)))
          disabledProviders.Add(provider);

    var changed = false;
    if (disabledProviders.Count > 0)
    {
      ImGuiHelper.TextWarning($"{disabledProviders.Count} required mod(s) are installed but disabled");
      ImGui.SameLine();
      if (ImGui.Button($"Enable {disabledProviders.Count} disabled dependenc{(disabledProviders.Count == 1 ? "y" : "ies")}"))
      {
        var count = modList.EnableDisabledDependencies();
        Logger.Global.LogInfo($"Enabled {count} missing dependenc{(count == 1 ? "y" : "ies")}");
        changed = count > 0;
      }
      ImGuiHelper.ItemTooltip("Enable every installed mod that an enabled mod depends on (follows chains).");
    }

    return changed;
  }

  private static void DrawNode(ModInfo mod, Dictionary<ModInfo, DepInfo> info,
    HashSet<ModInfo> visible, HashSet<ModInfo> path, ref int counter, ref ModInfo selected)
  {
    var dep = info[mod];
    var children = dep.Dependents
      .Where(visible.Contains)
      .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
      .ToList();
    var hasExtras = dep.Missing.Count > 0 || dep.Disabled.Count > 0;
    var isLeaf = children.Count == 0 && !hasExtras;

    ImGui.PushID(counter++);

    // Guard against dependency cycles.
    if (path.Contains(mod))
    {
      ImGui.Bullet();
      ImGui.SameLine();
      ImGuiHelper.TextWarning($"{mod.Name} (cycle)");
      ImGui.PopID();
      return;
    }

    if (isLeaf)
    {
      DrawLeafRow(mod, ref selected);
      ImGui.PopID();
      return;
    }

    var flags = ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.OpenOnArrow
      | ImGuiTreeNodeFlags.DefaultOpen;

    var open = ImGui.TreeNodeEx($"{mod.Name}", flags);
    if (ImGui.IsItemClicked())
      selected = mod;
    NodeTooltip(mod);

    if (open)
    {
      path.Add(mod);

      foreach (var modRef in dep.Missing)
      {
        ImGui.Bullet();
        ImGui.SameLine();
        ImGuiHelper.TextError($"missing: {modRef}");
      }
      foreach (var modRef in dep.Disabled)
      {
        ImGui.Bullet();
        ImGui.SameLine();
        ImGuiHelper.TextWarning($"disabled: {modRef}");
      }

      foreach (var child in children)
        DrawNode(child, info, visible, path, ref counter, ref selected);

      path.Remove(mod);
      ImGui.TreePop();
    }

    ImGui.PopID();
  }

  private static void DrawLeafRow(ModInfo mod, ref ModInfo selected)
  {
    ImGui.Bullet();
    ImGui.SameLine();
    if (ImGui.Selectable(mod.Name))
      selected = mod;
    NodeTooltip(mod);
  }

  private static void NodeTooltip(ModInfo mod)
  {
    if (!ImGui.IsItemHovered())
      return;
    var version = string.IsNullOrEmpty(mod.About?.Version) ? "?" : mod.About.Version;
    var author = string.IsNullOrEmpty(mod.Author) ? "?" : mod.Author;
    ImGuiHelper.TextTooltip(
      $"{mod.Name}\nSource: {mod.Source}\nAuthor: {author}\nVersion: {version}\n\nClick to view details");
  }

  // Resolves dependency edges among enabled mods, and records dependencies that are
  // missing (not installed at all) or only provided by a disabled mod.
  private static Dictionary<ModInfo, DepInfo> Resolve(List<ModInfo> enabled, List<ModInfo> all)
  {
    var info = new Dictionary<ModInfo, DepInfo>();
    foreach (var mod in enabled)
      info[mod] = new DepInfo();

    foreach (var mod in enabled)
    {
      if (mod.Source == ModSourceType.Core)
        continue;
      foreach (var modRef in mod.About?.DependsOn ?? [])
      {
        if (!modRef.IsValid)
          continue;

        var providers = enabled.Where(m => m != mod && m.Satisfies(modRef)).ToList();
        if (providers.Count > 0)
        {
          foreach (var provider in providers)
          {
            info[mod].DependsOn.Add(provider);
            info[provider].Dependents.Add(mod);
          }
        }
        else if (all.Any(m => m != mod && m.Satisfies(modRef)))
          info[mod].Disabled.Add(modRef); // installed but disabled
        else
          info[mod].Missing.Add(modRef);  // not installed at all
      }
    }

    return info;
  }

  // Returns the set of mods to render: everything when not searching, otherwise the
  // mods matching the search plus their (transitive) dependencies, so the path stays intact.
  private static HashSet<ModInfo> ComputeVisible(List<ModInfo> enabled,
    Dictionary<ModInfo, DepInfo> info, string search)
  {
    var visible = new HashSet<ModInfo>();
    if (string.IsNullOrWhiteSpace(search))
    {
      foreach (var mod in enabled)
        visible.Add(mod);
      return visible;
    }

    var q = search.Trim();
    bool matches(ModInfo m) =>
      (m.Name?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
      || (m.Author?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);

    void markUp(ModInfo m)
    {
      if (!visible.Add(m))
        return;
      foreach (var parent in info[m].DependsOn)
        markUp(parent);
    }

    foreach (var mod in enabled)
      if (matches(mod))
        markUp(mod);

    return visible;
  }
}
