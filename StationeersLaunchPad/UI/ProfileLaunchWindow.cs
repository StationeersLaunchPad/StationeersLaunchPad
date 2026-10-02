using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using StationeersLaunchPad.Metadata;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public enum ProfileLaunchAction
{
  None,
  // load the active pack and start the game without waiting
  Continue,
  OpenMenu,
}

public static class ProfileLaunchWindow
{
  private static bool switched;

  public static ProfileLaunchAction Draw(
    LoadStage stage, ProfileManager manager, ModList modList, out bool profileChanged)
  {
    profileChanged = false;
    if (stage != LoadStage.Configuring || manager.ActiveProfile == null)
      return ProfileLaunchAction.None;

    var action = ProfileLaunchAction.None;
    var changed = false;
    ImGuiHelper.Draw(() =>
    {
      var style = ImGui.GetStyle();
      var active = manager.ActiveProfile;
      var missing = ProfileManager.GetMissingMods(active, modList);
      var workshopMissing = missing.Where(entry => entry.WorkshopHandle > 1).ToList();
      if (missing.Count > 0)
        switched = true;

      var buttonHeight = ImGui.GetFrameHeight() * 1.2f;
      var galleryHeight = Mathf.Round(ImGui.GetTextLineHeight() * 9f);
      var warnHeight = missing.Count > 0 ? ImGui.GetTextLineHeightWithSpacing() : 0f;
      var height = buttonHeight + style.ItemSpacing.y + galleryHeight + warnHeight + style.WindowPadding.y * 2f;

      var screen = ImGuiHelper.ScreenRect().Shrink(25f);
      screen.SplitOY(-100f, out _, out var bottomRect);
      ImGui.SetNextWindowPos(new Vector2(screen.Min.x, bottomRect.Min.y - 8f), ImGuiCond.Always, new Vector2(0f, 1f));
      ImGui.SetNextWindowSize(new Vector2(screen.Size.x, height));
      ImGui.Begin("##profilelaunch",
        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);

      var headerY = ImGui.GetCursorPosY();
      ImGui.SetCursorPosY(headerY + (buttonHeight - ImGui.GetTextLineHeight()) / 2f);
      ImGuiHelper.TextColored("MOD PACK", LaunchPadTheme.TextMuted);
      ImGui.SameLine();
      ImGuiHelper.TextColored(active.Name, LaunchPadTheme.Accent);
      DrawHeaderAction(active, workshopMissing, missing.Count, headerY, buttonHeight, ref action);
      ImGui.SetCursorPosY(headerY + buttonHeight + style.ItemSpacing.y);

      var picked = PackGallery.Draw("##launchgallery", manager, modList,
        new Vector2(ImGui.GetContentRegionAvail().x, galleryHeight), false, out _,
        enabled: !ProfilePanel.Busy);
      if (picked != null && manager.ApplyProfile(picked.Name, modList))
      {
        changed = true;
        switched = true;
      }

      if (missing.Count > 0)
        ImGuiHelper.TextColored(
          $"{active.Name} needs {missing.Count} mod{(missing.Count == 1 ? "" : "s")} that {(missing.Count == 1 ? "isn't" : "aren't")} installed: "
          + string.Join(", ", missing.Take(3).Select(ProfilePanel.GetFallbackName))
          + (missing.Count > 3 ? $" and {missing.Count - 3} more" : ""),
          LaunchPadTheme.Warn);

      ImGui.End();
    });
    profileChanged = changed;
    return action;
  }

  // download or open the menu when mods are missing, start now after a switch
  private static void DrawHeaderAction(ProfileData active, List<ProfileModEntry> workshopMissing, int missingCount,
    float y, float height, ref ProfileLaunchAction action)
  {
    var style = ImGui.GetStyle();
    var right = ImGui.GetWindowContentRegionMax().x;
    if (missingCount > 0)
    {
      var menuWidth = ImGui.CalcTextSize("Open SLP Menu").x + style.FramePadding.x * 4f;
      var downloadText = !Steam.Running ? "Steam isn't running"
        : ProfilePanel.Busy ? ProfilePanel.BusyText
        : $"Download {workshopMissing.Count} mod{(workshopMissing.Count == 1 ? "" : "s")}";
      var downloadWidth = workshopMissing.Count > 0 ? ImGui.CalcTextSize(downloadText).x + style.FramePadding.x * 6f : 0f;
      ImGui.SameLine(right - menuWidth - (downloadWidth > 0f ? downloadWidth + style.ItemSpacing.x : 0f));
      ImGui.SetCursorPosY(y);
      if (downloadWidth > 0f)
      {
        if (Widgets.PrimaryButton($"{downloadText}##download", new Vector2(downloadWidth, height),
          Steam.Running && !ProfilePanel.Busy, 1f))
          ProfilePanel.Download(workshopMissing);
        if (!Steam.Running)
          ImGuiHelper.ItemTooltip(Steam.NotRunningText);
        ImGui.SameLine();
      }
      if (ImGui.Button("Open SLP Menu", new Vector2(menuWidth, height)))
        action = ProfileLaunchAction.OpenMenu;
      ImGuiHelper.ItemTooltip("Open the full LaunchPad window to remove the mods or pick another pack.");
    }
    else if (switched)
    {
      var text = $"Load {active.Name} & start now";
      var width = ImGui.CalcTextSize(text).x + style.FramePadding.x * 6f;
      ImGui.SameLine(right - width);
      ImGui.SetCursorPosY(y);
      if (Widgets.PrimaryButton($"{text}##loadnow", new Vector2(width, height), true, 1f))
        action = ProfileLaunchAction.Continue;
      ImGuiHelper.ItemTooltip("Skip the countdown. Otherwise auto load carries on with this pack.");
    }
    else
    {
      ImGui.SameLine();
      ImGuiHelper.TextRightDisabled("Click a pack to switch");
    }
  }
}
