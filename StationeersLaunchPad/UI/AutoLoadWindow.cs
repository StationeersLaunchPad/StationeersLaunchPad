
using ImGuiNET;
using System;

namespace StationeersLaunchPad.UI
{
  public class AutoLoadWindow
  {
    // returns true if the user clicked to stop autoloading
    public static bool Draw(LoadStage stage, StageWait wait)
    {
      var stopAuto = false;
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
          LoadStage.Searching => "Finding Mods",
          LoadStage.Configuring => $"Loading Mods in {wait.SecondsRemaining:0.0}s",
          LoadStage.Loading => "Loading Mods",
          LoadStage.Loaded => $"Starting game in {wait.SecondsRemaining:0.0}s",
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

        if (ImGui.IsWindowHovered())
        {
          ImGuiHelper.TextTooltip("Click to pause loading.");
          if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            stopAuto = true;
        }
        ;

        ImGui.End();
      });

      return stopAuto;
    }
  }
}