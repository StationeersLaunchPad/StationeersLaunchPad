using System;
using System.Linq;
using ImGuiNET;
using StationeersLaunchPad.Metadata;
using UnityEngine;

namespace StationeersLaunchPad.UI;

// Colored banner summarising the pre-load health check. Returns a mod the user clicked
// (to inspect in the Mod Info tab), or null.
public static class PreLoadCheckPanel
{
  public static (ModInfo selected, bool changed) DrawBanner(ModList modList)
  {
    if (PreLoadCheck.Current == null)
      PreLoadCheck.Run(modList);
    var result = PreLoadCheck.Current;
    if (result == null)
      return (null, false);

    ModInfo selected = null;
    var changed = false;

    if (result.Ok)
    {
      ImGuiHelper.TextSuccess("Pre-load check: no problems detected");
      if (result.Infos > 0
          && ImGui.TreeNodeEx($"{result.Infos} note(s)###preloadnotes", ImGuiTreeNodeFlags.SpanAvailWidth))
      {
        DrawIssues(result, ref selected);
        ImGui.TreePop();
      }
      ImGui.Separator();
      return (selected, changed);
    }

    var color = result.Errors > 0 ? ImGuiHelper.Red : ImGuiHelper.Yellow;
    ImGuiHelper.TextColored(
      $"Pre-load check: {result.Errors} error(s), {result.Warnings} warning(s)", color);

    changed |= DrawActions(modList, result);

    if (ImGui.TreeNodeEx($"Details ({result.Issues.Count})###preloaddetails",
        ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.DefaultOpen))
    {
      DrawIssues(result, ref selected);
      ImGui.TreePop();
    }

    ImGui.Separator();
    return (selected, changed);
  }

  // One-click fixes for the detected problems.
  private static bool DrawActions(ModList modList, CheckResult result)
  {
    var changed = false;
    var any = false;

    changed |= DisableButton(result, "Incompatible",
      "Disable {0} incompatible mod(s)",
      "Disable mods whose code targets game members that no longer exist (incompatible with this game version).",
      ref any);

    changed |= DisableButton(result, "Previous failure",
      "Disable {0} previously-failed mod(s)",
      "Turn off every mod that failed to load in a previous session.",
      ref any);

    return changed;
  }

  private static bool DisableButton(CheckResult result, string category, string labelFormat, string tooltip, ref bool any)
  {
    var mods = result.Issues
      .Where(i => i.Category == category && i.Mod != null)
      .Select(i => i.Mod)
      .Distinct()
      .ToList();
    if (mods.Count == 0)
      return false;

    if (any)
      ImGui.SameLine();
    any = true;

    var changed = false;
    if (ImGui.Button(string.Format(labelFormat, mods.Count)))
    {
      foreach (var mod in mods)
        mod.Enabled = false;
      Logger.Global.LogInfo($"Disabled {mods.Count} mod(s) ({category})");
      changed = true;
    }
    ImGuiHelper.ItemTooltip(tooltip);
    return changed;
  }

  private static void DrawIssues(CheckResult result, ref ModInfo selected)
  {
    var idx = 0;
    foreach (var issue in result.Issues
      .OrderByDescending(i => (int)i.Severity)
      .ThenBy(i => i.Category, StringComparer.OrdinalIgnoreCase))
    {
      ImGui.PushID(idx++);

      var color = issue.Severity switch
      {
        CheckSeverity.Error => ImGuiHelper.Red,
        CheckSeverity.Warning => ImGuiHelper.Yellow,
        _ => ImGuiHelper.TextDisabledColor,
      };

      ImGui.Bullet();
      ImGui.SameLine();
      ImGui.PushStyleColor(ImGuiCol.Text, (Vector4)color);
      if (ImGui.Selectable($"[{issue.Category}] {issue.Message}") && issue.Mod != null)
        selected = issue.Mod;
      ImGui.PopStyleColor();

      ImGui.PopID();
    }
  }
}
