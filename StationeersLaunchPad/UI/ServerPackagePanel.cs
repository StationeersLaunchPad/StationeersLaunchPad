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
    ImGui.PushTextWrapPos(0f);
    ImGuiHelper.TextColored(
      "Zips a pack's mods together with a modconfig.xml, ready to drop into a dedicated server.",
      LaunchPadTheme.TextMuted);
    ImGui.PopTextWrapPos();

    var active = manager.ActiveProfile;
    var mods = manager.ServerPackageMods(modList).Where(mod => mod.Source != ModSourceType.Core).ToList();
    var missing = active == null ? [] : ProfileManager.GetMissingMods(active, modList);
    var leftOut = manager.ClientsideActive ? modList.AllMods.Where(manager.IsClientside).ToList() : [];

    ImGui.PushTextWrapPos(0f);
    if (mods.Count == 0)
      ImGuiHelper.TextColored($"{active?.Name ?? "This pack"} has no mods to export.", LaunchPadTheme.TextSub);
    else
    {
      ImGuiHelper.Text($"Exports the {mods.Count} mod{(mods.Count == 1 ? "" : "s")} of {active?.Name}. Check them before exporting.");
      if (ImGui.SmallButton(ManualLoadWindow.ShowingServerPackageList ? "Back to the normal mod list" : "Show them in the mod list"))
        ManualLoadWindow.ToggleServerPackageList();
      if (leftOut.Count > 0)
        ImGuiHelper.TextColored(
          $"Clientside mods are left out: {NameList(leftOut.Select(mod => mod.Name))}",
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
