using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using ImGuiNET;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public static class ServerPackagePanel
{
  private static bool exporting;
  private static string exportedPath;
  private static string exportError;

  public static void Draw(ProfileManager manager, ModList modList)
  {
    Widgets.PageHeader("Server Package",
      "Zip a pack's mods together with a modconfig.xml, ready to drop into a dedicated server.");

    var active = manager.ActiveProfile;
    var mods = manager.ServerPackageMods(modList).Where(mod => mod.Source != ModSourceType.Core).ToList();
    var missing = active == null ? [] : ProfileManager.GetMissingMods(active, modList);
    var leftOut = manager.AlwaysOnActive ? modList.AllMods.Where(manager.IsAlwaysOn).ToList() : [];

    Widgets.SectionHeader("Pack");
    ImGuiHelper.TextColored(active?.Name ?? "No pack", LaunchPadTheme.Accent);
    ImGui.SameLine();
    ImGuiHelper.TextColored("pick another pack in the gallery below", LaunchPadTheme.TextMuted);

    ImGui.Spacing();
    ImGui.PushTextWrapPos(0f);
    if (mods.Count == 0)
      ImGuiHelper.TextColored("This pack has no mods to export.", LaunchPadTheme.TextSub);
    else
    {
      ImGuiHelper.Text($"The {mods.Count} mod{(mods.Count == 1 ? "" : "s")} listed on the left go into the package. Check them before exporting.");
      if (leftOut.Count > 0)
        ImGuiHelper.TextColored(
          $"Always-on mods are left out, they are meant to be client-side: {NameList(leftOut.Select(mod => mod.Name))}",
          LaunchPadTheme.TextMuted);
    }
    if (missing.Count > 0)
      ImGuiHelper.TextColored(
        $"Not installed, so they can't be included: {NameList(missing.Select(ProfilePanel.GetFallbackName))}",
        LaunchPadTheme.Warn);
    ImGui.PopTextWrapPos();

    ImGui.Spacing();
    var label = exporting ? "Exporting..." : $"Export {mods.Count} mod{(mods.Count == 1 ? "" : "s")}";
    var size = new Vector2(Math.Min(ImGui.GetContentRegionAvail().x, 360f), ImGui.GetFrameHeight() * 1.8f);
    if (Widgets.PrimaryButton($"{label}##export", size, mods.Count > 0 && !exporting, 1.1f))
      Export(manager.ServerPackageMods(modList)).Forget();

    if (exportError != null)
    {
      ImGui.PushTextWrapPos(0f);
      ImGuiHelper.TextColored($"Export failed: {exportError}", LaunchPadTheme.Err);
      ImGui.PopTextWrapPos();
    }
    else if (exportedPath != null)
    {
      ImGui.PushTextWrapPos(0f);
      ImGuiHelper.TextColored($"Saved {exportedPath}", LaunchPadTheme.Ok);
      ImGui.PopTextWrapPos();
      if (ImGui.SmallButton("Show in folder"))
        ProcessUtil.OpenExplorerSelectFile(exportedPath);
    }

    Widgets.SectionHeader("On the server");
    ImGui.PushTextWrapPos(0f);
    ImGuiHelper.TextColored(
      "Extract the zip into the dedicated server folder, next to rocketstation_DedicatedServer.exe. "
      + "It adds a modconfig.xml and a mods folder. Servers load exactly these mods and don't use packs.",
      LaunchPadTheme.TextSub);
    ImGui.PopTextWrapPos();
  }

  private static async UniTaskVoid Export(List<ModInfo> mods)
  {
    exporting = true;
    exportedPath = null;
    exportError = null;
    try
    {
      exportedPath = await UniTask.RunOnThreadPool(() => LaunchPadConfig.ExportModPackage(mods));
      Logger.Global.Log($"Exported server package {exportedPath}");
      ProcessUtil.OpenExplorerSelectFile(exportedPath);
    }
    catch (Exception ex)
    {
      exportError = ex.Message;
      Logger.Global.LogException(ex);
    }
    finally
    {
      exporting = false;
    }
  }

  private static string NameList(IEnumerable<string> names)
  {
    var list = names.ToList();
    var text = string.Join(", ", list.Take(5));
    return list.Count > 5 ? $"{text} and {list.Count - 5} more" : text;
  }
}
