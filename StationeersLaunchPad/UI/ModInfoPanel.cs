using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public class ModInfoPanel
{
  internal const string MetaSeparator = "    ";

  public static bool Draw(ModInfo mod, LoadStage stage, ModList modList)
  {
    if (mod == null)
    {
      ImGuiHelper.TextDisabled("Select a mod on the left to view its details.");
      return false;
    }

    var about = mod.About;
    var rdef = mod.Def as RepoModDefinition;

    var changed = DrawHeader(mod, about, rdef, stage, modList);

    if (about == null)
    {
      ImGui.Spacing();
      ImGuiHelper.TextWarning("This mod has no About/About.xml.");
      Widgets.SectionHeader("Details");
      DrawDetails(mod, rdef);
      return changed;
    }

    Widgets.SectionHeader("Details");
    DrawDetails(mod, rdef);

    if (about.Tags is { Count: > 0 })
    {
      Widgets.SectionHeader("Tags");
      Widgets.Chips(about.Tags.Select(tag => tag.Trim()).Where(tag => tag.Length > 0), LaunchPadTheme.TextSub);
    }

    if (about.DependsOn is { Count: > 0 } || about.OrderBefore is { Count: > 0 }
      || about.OrderAfter is { Count: > 0 })
    {
      Widgets.SectionHeader("Load order");
      DrawRefs("Depends on", about.DependsOn);
      DrawRefs("Loads before", about.OrderBefore);
      DrawRefs("Loads after", about.OrderAfter);
    }

    if (!string.IsNullOrWhiteSpace(about.ChangeLog))
    {
      Widgets.SectionHeader("Changelog");
      ImGuiHelper.TextPretty(about.ChangeLog);
    }

    if (!string.IsNullOrWhiteSpace(about.Description))
    {
      Widgets.SectionHeader("Description");
      ImGuiHelper.TextPretty(about.Description);
    }

    Widgets.SectionHeader("Assemblies");
    if (mod.Assemblies == null || mod.Assemblies.Count == 0)
      ImGuiHelper.TextDisabled("No assemblies found");
    else
      foreach (var assembly in mod.Assemblies)
        ImGuiHelper.TextDisabled(assembly.Trim());
    return changed;
  }

  private static bool DrawHeader(ModInfo mod, ModAboutEx about, RepoModDefinition rdef, LoadStage stage, ModList modList)
  {
    var lineHeight = ImGui.GetTextLineHeightWithSpacing();
    var imageHeight = Mathf.Clamp(lineHeight * 5f, 84f, 140f);
    var imageWidth = imageHeight * 16f / 9f;
    var start = ImGui.GetCursorScreenPos();
    var drawList = ImGui.GetWindowDrawList();

    var imageMax = start + new Vector2(imageWidth, imageHeight);
    ModImages.DrawFill(drawList, mod, start, imageMax);
    drawList.AddRect(start, imageMax, ImGui.ColorConvertFloat4ToU32((Vector4)LaunchPadTheme.Border));

    var textX = start.x + imageWidth + ImGui.GetStyle().ItemSpacing.x * 2f;
    ImGui.SetCursorScreenPos(new Vector2(textX, start.y));
    ImGui.BeginGroup();

    ImGui.SetWindowFontScale(1.3f);
    var titleStart = ImGui.GetCursorScreenPos();
    ImGuiHelper.Text(mod.Name);
    drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), titleStart + new Vector2(1f, 0f),
      ImGui.GetColorU32(ImGuiCol.Text), mod.Name ?? "");
    ImGui.SetWindowFontScale(1f);

    var meta = new List<string>();
    if (!string.IsNullOrWhiteSpace(about?.Version))
      meta.Add($"v{about.Version.Trim().TrimStart('v', 'V')}");
    meta.Add(mod.Source.ToString());
    if (!string.IsNullOrWhiteSpace(about?.Author))
    {
      ImGuiHelper.TextColored($"by {about.Author.Trim()}", LaunchPadTheme.TextSub);
      if (TrustedModders.IsTrusted(about.Author))
      {
        ImGui.SameLine(0f, 4f);
        Widgets.TrustedBadge();
      }
      ImGui.SameLine(0f, ImGui.CalcTextSize(MetaSeparator).x);
    }
    ImGui.PushTextWrapPos(0f);
    ImGuiHelper.TextColored(string.Join(MetaSeparator, meta), LaunchPadTheme.TextSub);
    ImGui.PopTextWrapPos();

    var badges = new List<(string, Color)>();
    if (mod.IsBetaProgramMod)
      badges.Add(("BETA", LaunchPadTheme.Warn));
    if (mod.HasBetaProgram)
      badges.Add(("Beta available", LaunchPadTheme.Info));
    ImGui.Spacing();
    foreach (var (label, color) in badges)
    {
      Widgets.Chip(label, color);
      ImGui.SameLine();
    }
    if (mod.Source != ModSourceType.Core)
    {
      var (_, label, guess, reason) = Loading.ModScan.Side(mod);
      Widgets.Chip(label, guess ? LaunchPadTheme.TextMuted : LaunchPadTheme.TextSub);
      ImGuiHelper.ItemTooltip(guess ? $"SLP's guess. {reason}" : reason, 400f);
    }
    ImGui.NewLine();

    ImGui.Spacing();
    if (ImGui.Button("Open folder"))
      ProcessUtil.OpenExplorerDir(mod.DirectoryPath);
    if (mod.WorkshopHandle > 1)
    {
      ImGui.SameLine();
      if (ImGui.Button("Workshop page"))
        Steam.OpenWorkshopPage(mod.WorkshopHandle);
    }
    if (mod.HasBetaProgram)
    {
      ImGui.SameLine();
      if (ImGui.Button("Beta workshop page"))
        Steam.OpenWorkshopPage(mod.BetaWorkshopHandle);
    }
    if (rdef != null && rdef.Mod.RepoID.StartsWith("github.com/"))
    {
      ImGui.SameLine();
      if (ImGui.Button("Repo"))
        Application.OpenURL($"https://{rdef.Mod.RepoID}");
    }
    var changed = BetaProgramsPanel.DrawModControls(stage, modList, mod);

    ImGui.EndGroup();

    var bottom = Math.Max(imageMax.y, ImGui.GetItemRectMax().y);
    ImGui.SetCursorScreenPos(new Vector2(start.x, bottom));
    ImGui.Dummy(new Vector2(0f, ImGui.GetStyle().ItemSpacing.y));
    return changed;
  }

  private static void DrawDetails(ModInfo mod, RepoModDefinition rdef)
  {
    var rows = new List<(string, string)>();
    if (!string.IsNullOrEmpty(mod.ModID))
      rows.Add(("Mod ID", mod.ModID));
    if (mod.WorkshopHandle > 1)
      rows.Add(("Workshop ID", $"{mod.WorkshopHandle}"));
    if (mod.HasBetaProgram)
      rows.Add(("Beta ID", $"{mod.BetaWorkshopHandle}"));
    if (rdef != null)
    {
      rows.Add(("Repo", rdef.Mod.RepoID));
      if (!string.IsNullOrEmpty(rdef.Mod.Branch))
        rows.Add(("Branch", rdef.Mod.Branch));
      rows.Add(("Min version", rdef.Mod.MinVersion));
      if (!string.IsNullOrEmpty(rdef.Mod.MaxVersion))
        rows.Add(("Max version", rdef.Mod.MaxVersion));
    }
    if (!string.IsNullOrEmpty(mod.DirectoryPath))
      rows.Add(("Path", mod.DirectoryPath));

    var labelWidth = rows.Max(row => ImGui.CalcTextSize(row.Item1).x) + ImGui.GetStyle().ItemSpacing.x * 3f;
    var x = ImGui.GetCursorPosX();
    foreach (var (label, value) in rows)
    {
      ImGuiHelper.TextColored(label, LaunchPadTheme.TextMuted);
      ImGui.SameLine(x + labelWidth);
      ImGui.PushTextWrapPos(0f);
      ImGuiHelper.Text(value ?? "");
      ImGui.PopTextWrapPos();
    }
  }

  private static void DrawRefs(string label, List<ModReference> refs)
  {
    if (refs == null || refs.Count == 0)
      return;
    ImGuiHelper.TextColored(label, LaunchPadTheme.TextMuted);
    ImGui.Indent();
    var allMods = LaunchPadConfig.ModList?.AllMods;
    foreach (var modRef in refs)
    {
      // show the installed mod's name instead of a bare id/handle when we have it
      var matches = allMods?.Where(mod => mod.Satisfies(modRef)).ToList() ?? [];
      var match = matches.FirstOrDefault(mod => mod.Enabled) ?? matches.FirstOrDefault();
      if (match == null)
      {
        ImGuiHelper.Text($"{modRef}");
        ImGui.SameLine();
        ImGuiHelper.TextColored("not installed", LaunchPadTheme.Err);
        continue;
      }
      ImGuiHelper.Text(match.Name);
      ImGuiHelper.ItemTooltip($"{modRef}");
      ImGui.SameLine();
      if (match.Enabled)
        ImGuiHelper.TextColored("enabled", LaunchPadTheme.Ok);
      else
        ImGuiHelper.TextColored("installed, not enabled", LaunchPadTheme.Warn);
    }
    ImGui.Unindent();
  }

}
