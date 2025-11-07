using ImGuiNET;

namespace StationeersLaunchPad
{
  public static class LaunchPadConsoleGUI
  {
    private static ulong lastLineCount = 0;
    private static Logger lastLogger = null;
    public static void DrawConsole(Logger logger)
    {
      LaunchPadConfigGUI.DrawEnumEntry(Configs.LogSeverities, Configs.LogSeverities.Value);
      ImGui.BeginChild("##logs", ImGuiWindowFlags.HorizontalScrollbar);

      var shouldScroll = false;
      if (logger != lastLogger || logger.TotalCount != lastLineCount)
      {
        lastLogger = logger;
        lastLineCount = logger.TotalCount;
        shouldScroll = Configs.AutoScrollLogs.Value;
      }

      for (var i = 0; i < logger.Count; i++)
      {
        DrawConsoleLine(logger[i]);
      }

      if (shouldScroll)
      {
        shouldScroll = false;
        ImGui.SetScrollHereY();
      }

      ImGuiHelper.DrawIfHovering(() =>
      {
        ImGuiHelper.TextTooltip("Right-click to copy logs.");
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
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

      if (!force && !Configs.LogSeverities.Value.HasFlag(line.Severity))
        return;

      var text = Configs.CompactLogs.Value ? line.CompactString : line.FullString;
      switch (line.Severity)
      {
        case LogSeverity.Debug:
          ImGuiHelper.TextDisabled(text);
          break;
        case LogSeverity.Information:
          ImGuiHelper.Text(text);
          break;
        case LogSeverity.Warning:
          ImGuiHelper.TextWarning(text);
          break;
        case LogSeverity.Error or LogSeverity.Exception or LogSeverity.Fatal:
          ImGuiHelper.TextError(text);
          break;
      }
      ;
    }
  }
}
