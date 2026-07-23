using ImGuiNET;
using StationeersLaunchPad.Metadata;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public enum ProfileStatusKind
{
  Saved,
  Unsaved,
  Info,
  Error,
}

public readonly struct ProfileStatusInfo(
  ProfileStatusKind kind, string label, string tooltip)
{
  public readonly ProfileStatusKind Kind = kind;
  public readonly string Label = label;
  public readonly string Tooltip = tooltip;
}

public static class ProfileStatusIndicator
{
  public static ProfileStatusInfo Evaluate(
    ProfileManager manager, ProfileData profile, ModList modList)
  {
    if (profile == null)
      return new(ProfileStatusKind.Error, "Profile unavailable",
        "The selected profile could not be loaded.");

    var modIndex = ProfileManager.BuildModIndex(modList.AllMods);
    var missing = manager.GetMissingMods(profile.Name, modList).Count;
    var diverged = manager.HasDiverged(profile.Name, modList, modIndex);

    if (missing > 0)
      return new(ProfileStatusKind.Error,
        $"{missing} required mod{(missing == 1 ? " is" : "s are")} missing",
        "Loading is paused. Restore the missing mods or remove them from this profile.");
    if (diverged)
      return new(ProfileStatusKind.Unsaved, "Unsaved changes",
        "The working mod list differs from this saved profile. Save it or revert the changes.");
    if (ProfileManager.IsVanillaProfile(profile.Name))
      return new(ProfileStatusKind.Saved, "Built-in: Mods disabled",
        "Vanilla is a built-in profile that loads Stationeers without mods.");
    return new(ProfileStatusKind.Saved, "Saved", "This profile matches the working mod list.");
  }

  public static void Draw(ProfileStatusInfo status, bool prominent = false) =>
    Draw(status.Kind, status.Label, status.Tooltip, prominent);

  public static void Draw(
    ProfileStatusKind kind, string label, string tooltip = null, bool prominent = false)
  {
    var color = ColorFor(kind);
    var size = prominent ? 14f : 11f;
    var start = ImGui.GetCursorScreenPos();
    var lineHeight = ImGui.GetTextLineHeight();
    var center = start + new Vector2(size / 2f, lineHeight / 2f);
    var radius = size * 0.42f;
    var drawList = ImGui.GetWindowDrawList();
    var u32 = ImGui.ColorConvertFloat4ToU32((Vector4)color);

    switch (kind)
    {
      case ProfileStatusKind.Saved:
        drawList.AddQuadFilled(
          center + new Vector2(0, -radius), center + new Vector2(radius, 0),
          center + new Vector2(0, radius), center + new Vector2(-radius, 0), u32);
        break;
      case ProfileStatusKind.Unsaved:
        drawList.AddTriangleFilled(
          center + new Vector2(0, -radius),
          center + new Vector2(radius, radius),
          center + new Vector2(-radius, radius), u32);
        break;
      case ProfileStatusKind.Info:
        drawList.AddCircleFilled(center, radius, u32, 16);
        break;
      case ProfileStatusKind.Error:
        drawList.AddNgonFilled(center, radius, u32, 6);
        break;
    }

    ImGui.Dummy(new Vector2(size, lineHeight));
    ImGui.SameLine();
    ImGuiHelper.TextColored(label, color);
    if (!string.IsNullOrEmpty(tooltip))
      ImGuiHelper.ItemTooltip(tooltip);
  }

  public static Color ColorFor(ProfileStatusKind kind) => kind switch
  {
    ProfileStatusKind.Saved => LaunchPadTheme.Ok,
    ProfileStatusKind.Unsaved => LaunchPadTheme.Warn,
    ProfileStatusKind.Info => LaunchPadTheme.Info,
    ProfileStatusKind.Error => LaunchPadTheme.Err,
    _ => LaunchPadTheme.TextMuted,
  };
}
