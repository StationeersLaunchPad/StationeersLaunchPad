using ImGuiNET;

namespace StationeersLaunchPad
{
  public static class LaunchPadConsoleGUI
  {
    public static bool IsActive = false;

    public static void Draw()
    {
      if (!IsActive)
        return;

      ImGuiHelper.Draw(() => DrawConsole());
    }

    private static LogSeverity logSeverity => LaunchPadConfig.LogSeverities.Value;
    private static int lastLineCount = 0;
    internal static bool shouldScroll = false;
    public static void DrawConsole()
    {
      if (LaunchPadConfigGUI.DrawEnumEntry(LaunchPadConfig.LogSeverities, logSeverity))
        LaunchPadConfigGUI.ConfigChanged = true;
      ImGui.BeginChild("##logs", ImGuiWindowFlags.HorizontalScrollbar);

      var logger = LaunchPadLoaderGUI.SelectedLogger ?? Logger.Global;
      var buffer = logger.Buffer;
      var lines = buffer.Lines;
      var lineCount = buffer.TotalCount;

      if (lastLineCount != lineCount)
      {
        lastLineCount = lineCount;
        shouldScroll = LaunchPadConfig.AutoScroll;
      }

      foreach (var line in lines)
      {
        DrawConsoleLine(line);
      }

      if (shouldScroll)
      {
        shouldScroll = false;
        ImGui.SetScrollHereY();
      }

      ImGuiHelper.DrawIfHovering(() =>
      {
        ImGuiHelper.TextTooltip("Click to copy logs.");
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
          logger.CopyToClipboard();
          logger.Log("Logs copied to clipboard.");
        }
      });

      ImGui.EndChild();
    }

    public static void DrawConsoleLine(LogLine line, bool force = false)
    {
      if (line == null)
        return;

      var text = line.ToString();
      switch (line.Severity)
      {
        case LogSeverity.Debug:
          // only display debug if user actually wants it
          if (logSeverity.HasFlag(LogSeverity.Debug))
          {
            ImGuiHelper.TextDisabled(text);
          }
          break;
        case LogSeverity.Information:
          if (force || logSeverity.HasFlag(LogSeverity.Information))
          {
            ImGuiHelper.Text(text);
          }
          break;
        case LogSeverity.Warning:
          if (force || logSeverity.HasFlag(LogSeverity.Warning))
          {
            ImGuiHelper.TextWarning(text);
          }
          break;
        case LogSeverity.Error:
          if (force || logSeverity.HasFlag(LogSeverity.Error))
          {
            ImGuiHelper.TextError(text);
          }
          break;
        case LogSeverity.Exception:
          if (force || logSeverity.HasFlag(LogSeverity.Exception))
          {
            ImGuiHelper.TextError(text);
          }
          break;
        case LogSeverity.Fatal:
          if (force || logSeverity.HasFlag(LogSeverity.Fatal))
          {
            ImGuiHelper.TextError(text);
          }
          break;
      }
    }
  }
}
