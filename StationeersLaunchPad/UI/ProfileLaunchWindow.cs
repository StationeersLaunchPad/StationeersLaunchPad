using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using ImGuiNET;
using StationeersLaunchPad.Metadata;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public enum ProfileLaunchAction
{
  None,
  OpenSelector,
  Continue,
  OpenMenu,
}

public static class ProfileLaunchWindow
{
  private static string message = "";
  private static ProfileStatusKind messageKind = ProfileStatusKind.Saved;
  private static bool busy;

  public static ProfileLaunchAction Draw(
    LoadStage stage, ProfileManager manager, ModList modList,
    bool expanded, out bool profileChanged)
  {
    var changed = false;
    profileChanged = false;
    if (stage != LoadStage.Configuring || manager.ActiveProfile == null)
      return ProfileLaunchAction.None;

    var action = ProfileLaunchAction.None;
    ImGuiHelper.Draw(() =>
    {
      var profile = manager.ActiveProfile;
      var status = ProfileStatusIndicator.Evaluate(manager, profile, modList);
      var missingMods = manager.GetMissingMods(profile.Name, modList);

      var screen = ImGuiHelper.ScreenRect().Shrink(25f);
      screen.SplitOY(-100f, out _, out var bottomRect);
      var anchor = new Vector2(screen.Min.x, bottomRect.Min.y - 8f);
      var maxHeight = Math.Max(1f, anchor.y - screen.Min.y);
      ImGui.SetNextWindowPos(anchor, ImGuiCond.Always, new Vector2(0f, 1f));
      ImGui.SetNextWindowSizeConstraints(
        new Vector2(screen.Size.x, 0f), new Vector2(screen.Size.x, maxHeight));
      ImGui.Begin("##profilelaunch",
        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings
        | ImGuiWindowFlags.AlwaysAutoResize);

      var railColor = status.Kind == ProfileStatusKind.Saved
        ? LaunchPadTheme.Accent
        : ProfileStatusIndicator.ColorFor(status.Kind);
      var windowMin = ImGui.GetWindowPos();
      var windowMax = windowMin + ImGui.GetWindowSize();
      LaunchPadTheme.Fill(
        new Rect(windowMin, new Vector2(windowMin.x + 4f, windowMax.y)),
        railColor);
      ImGui.Indent(8f);

      ImGuiHelper.TextDisabled("ACTIVE MOD PROFILE");
      ImGuiHelper.TextColored(profile.Name, LaunchPadTheme.Accent);

      if (!expanded)
      {
        if (status.Kind == ProfileStatusKind.Error)
          ProfileStatusIndicator.Draw(status);
        else
        {
          ImGui.SameLine();
          ImGuiHelper.TextRightDisabled("Click to switch profile");
        }

        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
          action = ProfileLaunchAction.OpenSelector;
        if (ImGui.IsWindowHovered())
          ImGuiHelper.TextTooltip("Pause startup and choose which mod profile to load.");
        ImGui.Unindent(8f);
        ImGui.End();
        return;
      }

      ImGui.Spacing();
      ImGuiHelper.TextDisabled("Choose profile");
      ImGui.SetNextItemWidth(-float.Epsilon);
      if (ImGui.BeginCombo("##launchprofile", profile.Name))
      {
        foreach (var candidate in manager.AllProfiles)
        {
          var missing = manager.GetMissingMods(candidate.Name, modList).Count;
          var label = missing > 0
            ? $"{candidate.Name}  [{missing} missing]"
            : candidate.Name;
          if (missing > 0)
            ImGui.PushStyleColor(
              ImGuiCol.Text, (Vector4)ProfileStatusIndicator.ColorFor(ProfileStatusKind.Error));
          var selected = ImGui.Selectable(label, candidate == profile);
          if (missing > 0)
            ImGui.PopStyleColor();
          if (!selected || candidate == profile)
            continue;
          if (manager.ApplyProfile(candidate.Name, modList))
          {
            changed = true;
            message = $"Switched to {candidate.Name}";
            messageKind = ProfileStatusKind.Saved;
          }
          else
          {
            message = $"Could not load {candidate.Name}";
            messageKind = ProfileStatusKind.Error;
          }
        }
        ImGui.EndCombo();
      }

      profile = manager.ActiveProfile;
      missingMods = manager.GetMissingMods(profile.Name, modList);

      ImGui.Separator();
      if (missingMods.Count > 0)
        DrawMissingMods(manager, modList, profile, missingMods, ref changed, ref action);
      else
        DrawReadyActions(profile.Name, ref action);

      if (!string.IsNullOrEmpty(message))
      {
        ImGui.Spacing();
        ProfileStatusIndicator.Draw(messageKind, message);
      }

      ImGui.Unindent(8f);
      ImGui.End();
    });
    profileChanged = changed;
    return action;
  }

  private static void DrawMissingMods(
    ProfileManager manager, ModList modList, ProfileData profile,
    List<ProfileModEntry> missingMods, ref bool changed, ref ProfileLaunchAction action)
  {
    ProfileStatusIndicator.Draw(ProfileStatusKind.Error,
      $"{missingMods.Count} required mod{(missingMods.Count == 1 ? " is" : "s are")} missing");
    ImGuiHelper.TextDisabled(
      "Loading is paused. Restore missing Workshop mods or remove them from the profile.");
    DrawModNames(missingMods.Select(GetFallbackName), missingMods.Count);

    var workshopMods = missingMods
      .Where(entry => entry.WorkshopHandle > 1)
      .GroupBy(entry => entry.WorkshopHandle)
      .Select(group => group.First())
      .ToList();
    ImGui.Spacing();
    var available = ImGui.GetContentRegionAvail().x;
    var spacing = ImGui.GetStyle().ItemSpacing.x;
    var buttonCount = workshopMods.Count > 0 ? 2 : 1;
    var buttonWidth = (available - spacing * (buttonCount - 1)) / buttonCount;
    ImGui.BeginDisabled(busy);
    if (workshopMods.Count > 0)
    {
      var subscribeText = busy ? "Restoring Workshop Mods..." : "Resubscribe Workshop Mods";
      if (ImGui.Button(subscribeText, new Vector2(buttonWidth, 0)))
        Resubscribe(workshopMods).Forget();
      ImGui.SameLine();
    }
    if (ImGui.Button("Remove from Profile & Continue", new Vector2(buttonWidth, 0)))
    {
      if (manager.RemoveMods(profile.Name, missingMods)
        && manager.ApplyProfile(profile.Name, modList))
      {
        changed = true;
        message = $"Removed {missingMods.Count} missing mod{(missingMods.Count == 1 ? "" : "s")} from {profile.Name}";
        messageKind = ProfileStatusKind.Saved;
        action = ProfileLaunchAction.Continue;
      }
      else
      {
        message = $"Could not update {profile.Name}";
        messageKind = ProfileStatusKind.Error;
      }
    }
    ImGui.EndDisabled();

    if (missingMods.Any(entry => entry.WorkshopHandle <= 1))
      ImGuiHelper.TextDisabled(
        "Local and Repo mods must be reinstalled outside this dialog or removed from the profile.");
    DrawOpenMenuButton(ref action);
  }

  private static void DrawReadyActions(string profileName, ref ProfileLaunchAction action)
  {
    var available = ImGui.GetContentRegionAvail().x;
    var spacing = ImGui.GetStyle().ItemSpacing.x;
    var buttonWidth = (available - spacing) / 2f;
    if (ImGui.Button($"Load {profileName} & Continue", new Vector2(buttonWidth, 0)))
      action = ProfileLaunchAction.Continue;
    ImGui.SameLine();
    if (ImGui.Button("Open SLP Menu", new Vector2(buttonWidth, 0)))
      action = ProfileLaunchAction.OpenMenu;
    ImGuiHelper.ItemTooltip("Open the full LaunchPad menu on the Mod Profiles tab.");
  }

  private static void DrawOpenMenuButton(ref ProfileLaunchAction action)
  {
    ImGui.Spacing();
    ImGui.BeginDisabled(busy);
    if (ImGui.Button("Open SLP Menu"))
      action = ProfileLaunchAction.OpenMenu;
    ImGui.EndDisabled();
    ImGuiHelper.ItemTooltip("Open the full LaunchPad menu on the Mod Profiles tab.");
  }

  private static void DrawModNames(IEnumerable<string> names, int total)
  {
    var visible = names.Where(name => !string.IsNullOrWhiteSpace(name)).Take(4).ToList();
    foreach (var name in visible)
      ImGui.BulletText(name);
    if (total > visible.Count)
      ImGuiHelper.TextDisabled($"...and {total - visible.Count} more");
  }

  private static async UniTask Resubscribe(List<ProfileModEntry> workshopMods)
  {
    if (busy)
      return;
    busy = true;
    message = $"Restoring {workshopMods.Count} Workshop mod{(workshopMods.Count == 1 ? "" : "s")}...";
    messageKind = ProfileStatusKind.Info;
    try
    {
      foreach (var entry in workshopMods)
      {
        if (await Steam.SubscribeAndDownload(entry.WorkshopHandle))
          continue;
        message = $"Could not restore {GetFallbackName(entry)}";
        messageKind = ProfileStatusKind.Error;
        return;
      }

      message = "Workshop mods restored; refreshing the mod list";
      messageKind = ProfileStatusKind.Saved;
      LaunchPadConfig.ReloadMods();
    }
    catch (Exception ex)
    {
      Logger.Global.LogException(ex);
      message = "Workshop mods could not be restored";
      messageKind = ProfileStatusKind.Error;
    }
    finally
    {
      busy = false;
    }
  }

  private static string GetFallbackName(ProfileModEntry entry)
  {
    if (!string.IsNullOrEmpty(entry.Name))
      return entry.Name;
    if (!string.IsNullOrEmpty(entry.ModID))
      return entry.ModID;
    if (entry.WorkshopHandle > 1)
      return $"Workshop {entry.WorkshopHandle}";
    var name = Path.GetFileName(entry.DirectoryPath?.TrimEnd('/', '\\'));
    return string.IsNullOrEmpty(name) ? "Unknown mod" : name;
  }
}
