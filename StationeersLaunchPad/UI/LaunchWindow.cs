using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using StationeersLaunchPad.Loading;
using StationeersLaunchPad.Metadata;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public enum LaunchAction
{
  None,
  // load the active pack and start the game without waiting
  Continue,
  OpenMenu,
  OpenPacks,
}

// the box at the bottom of the splash: the packs, the startup status and the countdown bar
public static class LaunchWindow
{
  private static bool switched;

  // a status (the server check) replaces the stage text and locks the box
  public static LaunchAction Draw(LoadStage stage, StageWait wait, ProfileManager manager, ModList modList,
    string status, out bool profileChanged)
  {
    profileChanged = false;
    var input = status == null && !RocketBar.Diving && (stage == LoadStage.Configuring || stage == LoadStage.Loaded);
    var action = LaunchAction.None;

    if (input && (ImGui.IsKeyPressed(ImGuiKey.Space, false) || ImGui.IsKeyPressed(ImGuiKey.UpArrow, false)))
      action = LaunchAction.Continue;
    else if (input && wait.Auto && (ImGui.IsKeyPressed(ImGuiKey.Escape, false) || Input.GetKeyDown(KeyCode.P)))
      LaunchPadConfig.PauseAutoWait();
    else if (input && (Input.GetKeyDown(KeyCode.M) || ImGui.IsKeyPressed(ImGuiKey.DownArrow, false)))
      action = LaunchAction.OpenMenu;
    // unadvertised: the arrows steer the countdown
    else if (input && wait.Auto && ImGui.IsKeyPressed(ImGuiKey.LeftArrow))
      wait.Shift(1);
    else if (input && wait.Auto && ImGui.IsKeyPressed(ImGuiKey.RightArrow))
      wait.Shift(-1);
    if (Input.GetKeyDown(KeyCode.C))
      RocketBar.AlwaysCrash();

    var changed = false;
    ImGuiHelper.Draw(() =>
    {
      var style = ImGui.GetStyle();
      var active = manager.ActiveProfile;
      var showPacks = active != null
        && stage is LoadStage.Configuring or LoadStage.Loading or LoadStage.Loaded;
      var missing = showPacks ? ProfileManager.GetMissingMods(active, modList) : [];
      if (missing.Count > 0)
        switched = true;

      var lineHeight = ImGui.GetTextLineHeightWithSpacing();
      var buttonHeight = ImGui.GetFrameHeight() * 1.2f;
      var galleryHeight = Mathf.Round(ImGui.GetTextLineHeight() * 9f);
      var statusHeight = lineHeight + RocketBar.RowHeight;
      var height = statusHeight + style.WindowPadding.y * 2f;
      if (showPacks)
        height += buttonHeight + galleryHeight + style.ItemSpacing.y * 3f
          + (missing.Count > 0 ? lineHeight : 0f);

      var screen = ImGuiHelper.ScreenRect().Shrink(25f);
      ImGui.SetNextWindowPos(new Vector2(screen.Min.x, screen.Max.y), ImGuiCond.Always, new Vector2(0f, 1f));
      ImGui.SetNextWindowSize(new Vector2(screen.Size.x, height));
      ImGui.Begin("##launchwindow",
        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar);

      if (showPacks)
      {
        var locked = status != null || stage != LoadStage.Configuring || ProfilePanel.Busy;
        DrawHeader(active, missing, wait, input, buttonHeight, ref action);
        var picked = PackGallery.Draw("##launchgallery", manager, modList,
          new Vector2(ImGui.GetContentRegionAvail().x, galleryHeight), false, out _, enabled: !locked);
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
        ImGui.Dummy(new Vector2(0f, style.ItemSpacing.y));
      }

      DrawStatus(stage, wait, active, status);
      ImGui.End();
    });
    profileChanged = changed;
    return action;
  }

  private static void DrawHeader(ProfileData active, List<ProfileModEntry> missing, StageWait wait, bool input,
    float height, ref LaunchAction action)
  {
    var style = ImGui.GetStyle();
    var y = ImGui.GetCursorPosY();
    var textY = y + (height - ImGui.GetTextLineHeight()) / 2f;
    ImGui.SetCursorPosY(textY);
    ImGuiHelper.TextColored("MOD PACK", LaunchPadTheme.TextMuted);
    ImGui.SameLine();
    ImGuiHelper.TextColored(active.Name, LaunchPadTheme.Accent);

    if (Networking.Slp2ProfileSync.SyncStatus is { } sync)
    {
      ImGui.SameLine(0f, style.ItemSpacing.x * 3f);
      ImGui.SetCursorPosY(textY);
      var color = ProfileStatusIndicator.ColorFor(sync.kind);
      var dot = ImGui.GetCursorScreenPos() + new Vector2(4f, ImGui.GetTextLineHeight() / 2f);
      ImGui.GetWindowDrawList().AddCircleFilled(dot, 4f, ImGui.ColorConvertFloat4ToU32((Vector4)color), 12);
      ImGui.Dummy(new Vector2(8f, ImGui.GetTextLineHeight()));
      ImGui.SameLine();
      ImGuiHelper.TextColored(sync.text, color);
    }

    // right aligned: the key hints, then the buttons
    var workshopMissing = missing.Where(entry => entry.WorkshopHandle > 1).ToList();
    var buttons = new List<(float width, System.Action draw)>();
    var hints = new List<(string key, string label, System.Action click)>();
    var local = action;

    if (missing.Count > 0)
    {
      var downloadText = !Steam.Running ? "Steam isn't running"
        : ProfilePanel.Busy ? ProfilePanel.BusyText
        : $"Download {workshopMissing.Count} mod{(workshopMissing.Count == 1 ? "" : "s")}";
      var downloadWidth = ImGui.CalcTextSize(downloadText).x + style.FramePadding.x * 6f;
      if (workshopMissing.Count > 0)
        buttons.Add((downloadWidth, () =>
        {
          if (Widgets.PrimaryButton($"{downloadText}##download", new Vector2(downloadWidth, height),
            Steam.Running && !ProfilePanel.Busy, 1f))
            ProfilePanel.Download(workshopMissing);
          if (!Steam.Running)
            ImGuiHelper.ItemTooltip(Steam.NotRunningText);
        }));
      var menuWidth = ImGui.CalcTextSize("Open SLP Menu").x + style.FramePadding.x * 4f;
      buttons.Add((menuWidth, () =>
      {
        if (ImGui.Button("Open SLP Menu", new Vector2(menuWidth, height)))
          local = LaunchAction.OpenPacks;
        ImGuiHelper.ItemTooltip("Open the full LaunchPad window to remove the mods or pick another pack.");
      }));
    }
    else if (switched && input)
    {
      var text = $"Load {active.Name} & start now";
      var width = ImGui.CalcTextSize(text).x + style.FramePadding.x * 6f;
      buttons.Add((width, () =>
      {
        if (Widgets.PrimaryButton($"{text}##loadnow", new Vector2(width, height), true, 1f))
          local = LaunchAction.Continue;
        ImGuiHelper.ItemTooltip("Skip the countdown. Otherwise auto load carries on with this pack.");
      }));
    }

    if (input)
    {
      if (missing.Count == 0 && !switched)
        hints.Add(("Space", "Start now", () => local = LaunchAction.Continue));
      if (wait.Auto)
        hints.Add(("P", "Stay here", LaunchPadConfig.PauseAutoWait));
      if (missing.Count == 0)
        hints.Add(("M", "SLP Menu", () => local = LaunchAction.OpenMenu));
    }

    var hintWidths = hints.Select(hint => KeyHintWidth(hint.key, hint.label)).ToList();
    var x = ImGui.GetWindowContentRegionMax().x
      - buttons.Sum(button => button.width + style.ItemSpacing.x)
      - hintWidths.Sum(width => width + style.ItemSpacing.x * 2f);
    for (var i = 0; i < hints.Count; i++)
    {
      ImGui.SameLine();
      ImGui.SetCursorPos(new Vector2(x, y + (height - ImGui.GetFrameHeight()) / 2f));
      if (KeyHint(hints[i].key, hints[i].label))
        hints[i].click();
      x += hintWidths[i] + style.ItemSpacing.x * 2f;
    }
    foreach (var (width, draw) in buttons)
    {
      ImGui.SameLine();
      ImGui.SetCursorPos(new Vector2(x, y));
      draw();
      x += width + style.ItemSpacing.x;
    }

    action = local;
    ImGui.SetCursorPosY(y + height + style.ItemSpacing.y);
  }

  private static float KeyHintWidth(string key, string label)
  {
    var padding = ImGui.GetStyle().FramePadding.x;
    return ImGui.CalcTextSize(key).x + padding * 2f + padding + ImGui.CalcTextSize(label).x;
  }

  // a key cap and its label, clicking it does the same as the key
  private static bool KeyHint(string key, string label)
  {
    var padding = ImGui.GetStyle().FramePadding;
    var height = ImGui.GetFrameHeight();
    var keySize = new Vector2(ImGui.CalcTextSize(key).x + padding.x * 2f, height);
    var size = new Vector2(KeyHintWidth(key, label), height);
    var min = ImGui.GetCursorScreenPos();
    var clicked = ImGui.InvisibleButton($"##key{key}", size);
    var hovered = ImGui.IsItemHovered();
    var drawList = ImGui.GetWindowDrawList();
    var textY = min.y + (height - ImGui.GetTextLineHeight()) / 2f;
    drawList.AddRectFilled(min, min + keySize, LaunchPadTheme.OverU32(Color.white, hovered ? 0.16f : 0.08f), 3f);
    drawList.AddRect(min, min + keySize, LaunchPadTheme.OverU32(Color.white, hovered ? 0.45f : 0.25f), 3f);
    drawList.AddText(new Vector2(min.x + padding.x, textY), U32(LaunchPadTheme.Text), key);
    drawList.AddText(new Vector2(min.x + keySize.x + padding.x, textY),
      U32(hovered ? LaunchPadTheme.Text : LaunchPadTheme.TextSub), label);
    return clicked;
  }

  private static void DrawStatus(LoadStage stage, StageWait wait, ProfileData active, string status)
  {
    var countdown = status == null && wait.Auto && stage is LoadStage.Configuring or LoadStage.Loaded;
    var seconds = Mathf.Max(0, Mathf.CeilToInt((float)wait.SecondsRemaining));
    var pack = active?.Name ?? "mods";

    var (text, color) = status != null ? (status, LaunchPadTheme.Text)
      : stage switch
      {
        LoadStage.Updating => ("Checking for updates", LaunchPadTheme.TextSub),
        LoadStage.Initializing => ("Starting up", LaunchPadTheme.TextSub),
        LoadStage.News => ("Checking notices", LaunchPadTheme.TextSub),
        LoadStage.Searching => ("Finding mods", LaunchPadTheme.TextSub),
        LoadStage.Configuring when countdown => ($"Loading {pack} in {seconds}s", LaunchPadTheme.Text),
        LoadStage.Configuring => ("Paused", LaunchPadTheme.TextSub),
        LoadStage.Loading => (LoadingText(), LaunchPadTheme.Text),
        LoadStage.Loaded when countdown => ($"Starting game in {seconds}s", LaunchPadTheme.Text),
        LoadStage.Loaded => ("Mods loaded, paused", LaunchPadTheme.TextSub),
        _ => ("", LaunchPadTheme.TextSub),
      };
    ImGuiHelper.TextColored(text, color);
    ImGui.SameLine();
    ImGuiHelper.TextRightColored($"SLP {LaunchPadInfo.VERSION}", LaunchPadTheme.TextDim);

    if (status != null)
      RocketBar.Draw(1f, RocketBar.Rocket.Hold);
    else if (stage is LoadStage.Configuring or LoadStage.Loaded)
    {
      var fraction = countdown && wait.Seconds > 0 ? 1f - (float)(wait.SecondsRemaining / wait.Seconds) : 0f;
      RocketBar.Draw(fraction, countdown ? RocketBar.Rocket.Burn : RocketBar.Rocket.Pad);
    }
    else if (stage == LoadStage.Loading && LoadStrategy.StepsTotal > 0)
      RocketBar.Draw(LoadStrategy.StepsDone / (float)LoadStrategy.StepsTotal, RocketBar.Rocket.None);
    else
      RocketBar.Draw(0f, RocketBar.Rocket.None, indeterminate: true);
  }

  private static string LoadingText()
  {
    if (LoadStrategy.StepsTotal == 0 || LoadStrategy.Current == null)
      return "Loading mods";
    var percent = Mathf.RoundToInt(100f * LoadStrategy.StepsDone / LoadStrategy.StepsTotal);
    return $"Loading mods {percent}% - {LoadStrategy.Current}";
  }

  private static uint U32(Color color) => ImGui.ColorConvertFloat4ToU32((Vector4)color);
}
