using System;
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

      var lineHeight = ImGui.GetTextLineHeightWithSpacing();
      var galleryHeight = Mathf.Round(ImGui.GetTextLineHeight() * 9f);
      var actionsHeight = switched || missing.Count > 0 ? ImGui.GetFrameHeightWithSpacing() * 1.6f + lineHeight : 0f;
      var height = lineHeight + galleryHeight + actionsHeight + style.WindowPadding.y * 2f + style.ItemSpacing.y;

      var screen = ImGuiHelper.ScreenRect().Shrink(25f);
      screen.SplitOY(-100f, out _, out var bottomRect);
      ImGui.SetNextWindowPos(new Vector2(screen.Min.x, bottomRect.Min.y - 8f), ImGuiCond.Always, new Vector2(0f, 1f));
      ImGui.SetNextWindowSize(new Vector2(screen.Size.x, height));
      ImGui.Begin("##profilelaunch",
        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);

      ImGuiHelper.TextColored("MOD PACK", LaunchPadTheme.TextMuted);
      ImGui.SameLine();
      ImGuiHelper.TextColored(active.Name, LaunchPadTheme.Accent);
      ImGui.SameLine();
      ImGuiHelper.TextRightDisabled("Click a pack to switch");

      var picked = PackGallery.Draw("##launchgallery", manager, modList,
        new Vector2(ImGui.GetContentRegionAvail().x, galleryHeight), false, out _,
        enabled: !ProfilePanel.Busy);
      if (picked != null && manager.ApplyProfile(picked.Name, modList))
      {
        changed = true;
        switched = true;
      }

      if (missing.Count > 0)
      {
        // startup is paused now; once resolved, offer the button to carry on
        switched = true;
        ImGuiHelper.TextColored(
          $"{active.Name} needs {missing.Count} mod{(missing.Count == 1 ? "" : "s")} that {(missing.Count == 1 ? "isn't" : "aren't")} installed: "
          + string.Join(", ", missing.Take(3).Select(ProfilePanel.GetFallbackName))
          + (missing.Count > 3 ? $" and {missing.Count - 3} more" : ""),
          LaunchPadTheme.Warn);
        var buttonHeight = ImGui.GetFrameHeight() * 1.5f;
        if (workshopMissing.Count > 0)
        {
          if (Widgets.PrimaryButton(
            ProfilePanel.Busy ? $"{ProfilePanel.BusyText}##download" : $"Download {workshopMissing.Count} mod{(workshopMissing.Count == 1 ? "" : "s")}##download",
            new Vector2(Math.Min(420f, ImGui.GetContentRegionAvail().x * 0.4f), buttonHeight), !ProfilePanel.Busy, 1f))
            ProfilePanel.Download(workshopMissing);
          ImGui.SameLine();
        }
        if (ImGui.Button("Open SLP Menu", new Vector2(0f, buttonHeight)))
          action = ProfileLaunchAction.OpenMenu;
        ImGuiHelper.ItemTooltip("Open the full LaunchPad window to remove the mods or pick another pack.");
      }
      else if (switched)
      {
        if (Widgets.PrimaryButton($"Load {active.Name} & start now##loadnow",
          new Vector2(Math.Min(420f, ImGui.GetContentRegionAvail().x * 0.4f), ImGui.GetFrameHeight() * 1.5f), true, 1f))
          action = ProfileLaunchAction.Continue;
        ImGuiHelper.ItemTooltip("Skip the countdown. Otherwise auto load carries on with this pack.");
      }

      ImGui.End();
    });
    profileChanged = changed;
    return action;
  }
}
