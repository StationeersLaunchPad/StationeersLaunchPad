
using ImGuiNET;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;
using System.Collections.Generic;
using UnityEngine;

namespace StationeersLaunchPad.UI
{
  public class ModInfoPanel
  {
    public static void Draw(ModInfo mod)
    {
      if (mod == null)
      {
        ImGuiHelper.TextDisabled("Selected a mod to view detailed info");
        return;
      }

      var about = mod.About;

      ImGuiHelper.Text(mod.Name);

      if (ImGui.Button("Open Local Folder"))
        ProcessUtil.OpenExplorerDir(mod.DirectoryPath);

      if (mod.WorkshopHandle > 1)
      {
        ImGui.SameLine();
        if (ImGui.Button("Open Workshop Page"))
          Steam.OpenWorkshopPage(mod.WorkshopHandle);
      }

      var rdef = mod.Def as RepoModDefinition;
      if (rdef != null && rdef.Mod.RepoID.StartsWith("github.com/"))
      {
        ImGui.SameLine();
        if (ImGui.Button("Open Repo"))
          Application.OpenURL($"https://{rdef.Mod.RepoID}");
      }

      DrawOneLine("Source:", mod.Source.ToString());

      if (!string.IsNullOrEmpty(mod.DirectoryPath))
        DrawOneLine("Path:", mod.DirectoryPath);

      if (rdef != null)
      {
        DrawOneLine("Repo:", rdef.Mod.RepoID);
        if (!string.IsNullOrEmpty(rdef.Mod.Branch))
          DrawOneLine("\tBranch:", rdef.Mod.Branch);
        DrawOneLine("\tMinVersion:", rdef.Mod.MinVersion);
        if (!string.IsNullOrEmpty(rdef.Mod.MaxVersion))
          DrawOneLine("\tMaxVersion:", rdef.Mod.MaxVersion);
      }

      if (about == null)
      {
        ImGui.Spacing();
        ImGuiHelper.TextDisabled("Missing About.xml");
        return;
      }

      if (!string.IsNullOrEmpty(mod.ModID))
        DrawOneLine("ModID:", mod.ModID);
      if (mod.WorkshopHandle > 1)
        DrawOneLine("Workshop ID:", $"{mod.WorkshopHandle}");
      if (!string.IsNullOrEmpty(about.Author))
        DrawOneLine("Author", about.Author.Trim());
      if (!string.IsNullOrEmpty(about.Version))
        DrawOneLine("Version:", about.Version.Trim());
      if (!string.IsNullOrEmpty(about.ChangeLog))
        DrawNonEmptyLines("Changelog:", about.ChangeLog);
      if (!string.IsNullOrEmpty(about.Description))
        DrawNonEmptyLines("Description:", about.Description);
      DrawStringList("Tags:", about.Tags);
      DrawDepList("Depends On:", about.DependsOn);
      DrawDepList("Order Before:", about.OrderBefore);
      DrawDepList("Order After:", about.OrderAfter);
      DrawStringList("Assemblies:", mod.Assemblies, "\tNo Assemblies found");
    }

    private static void DrawOneLine(string title, string text)
    {
      ImGui.Spacing();
      ImGuiHelper.Text(title);
      ImGui.SameLine();
      ImGuiHelper.Text(text);
    }

    private static void DrawNonEmptyLines(string title, string text)
    {
      var lines = text.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
      if (lines.Length == 0)
        return;

      ImGui.Spacing();
      ImGuiHelper.Text(title);
      foreach (var line in lines)
        ImGuiHelper.Text(line);
    }

    private static void DrawStringList(
      string title, List<string> items, string emptyText = null)
    {
      if (items == null || items.Count == 0)
      {
        if (emptyText == null)
          return;
        ImGui.Spacing();
        ImGuiHelper.Text(title);
        ImGuiHelper.Text(emptyText);
        return;
      }
      ImGui.Spacing();
      ImGuiHelper.Text(title);
      foreach (var item in items)
      {
        ImGui.Spacing();
        ImGuiHelper.Text($"\t{item.Trim()}");
      }
    }

    private static void DrawDepList(string title, List<ModReference> deps)
    {
      if (deps == null || deps.Count == 0)
        return;
      ImGui.Spacing();
      ImGuiHelper.Text(title);
      foreach (var dep in deps)
      {
        ImGui.Spacing();
        ImGuiHelper.Text($"\t{dep}");
      }
    }
  }
}