
using System;
using ImGuiNET;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public class AutoLoadWindow
{
  // returns true if the user clicked to stop autoloading
  public static bool Draw(LoadStage stage, StageWait wait)
  {
    var openMenu = false;
    var acceptsStartupInput = stage == LoadStage.Configuring || stage == LoadStage.Loaded;

    if (acceptsStartupInput && ImGui.IsKeyPressed(ImGuiKey.Space, false))
      LaunchPadConfig.SkipAutoWaits();
    else if (acceptsStartupInput && wait.Auto
        && (ImGui.IsKeyPressed(ImGuiKey.Escape, false)
          || Input.GetKeyDown(KeyCode.P)))
      LaunchPadConfig.PauseAutoWait();
    else if (acceptsStartupInput && Input.GetKeyDown(KeyCode.M))
      openMenu = true;

    ImGuiHelper.Draw(() =>
    {
      var windowRect = ImGuiHelper.ScreenRect().Shrink(25f);
      windowRect.SplitOY(-100f, out _, out windowRect);
      ImGuiHelper.SetNextWindowRect(windowRect);
      ImGui.Begin("##preloaderauto", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);

      ImGuiHelper.Text($"StationeersLaunchPad {LaunchPadInfo.VERSION}");
      ImGuiHelper.Text(stage switch
      {
        LoadStage.Updating => "Checking for Update",
        LoadStage.Initializing => "Initializing",
        LoadStage.News => "Checking notices",
        LoadStage.Searching => "Finding Mods",
        LoadStage.Configuring when !wait.Auto => "Startup paused - Space to continue - M or click here to open the SLP Menu",
        LoadStage.Configuring => $"Loading Mods in {wait.SecondsRemaining:0.0}s - Space to continue - Esc/P to stay here - M or click here to open the SLP Menu",
        LoadStage.Loading => "Loading Mods",
        LoadStage.Loaded when !wait.Auto => "Startup paused - Space to continue - M or click here to open the SLP Menu",
        LoadStage.Loaded => $"Starting game in {wait.SecondsRemaining:0.0}s - Space to continue - Esc/P to stay here - M or click here to open the SLP Menu",
        LoadStage.Running => "Game Running",
        LoadStage.Failed => "Loading Failed",
        _ => throw new ArgumentOutOfRangeException(),
      });

      ImGui.Spacing();
      var line = Logger.Global.Last();
      if (line != null)
        LogPanel.DrawConsoleLine(line, true);
      else
        ImGuiHelper.Text("");

      if (ImGui.IsWindowHovered() && stage != LoadStage.News)
      {
        ImGuiHelper.TextTooltip("Click to open SLP.");
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
          openMenu = true;
      }

      ImGui.End();
    });

    return openMenu;
  }
}
