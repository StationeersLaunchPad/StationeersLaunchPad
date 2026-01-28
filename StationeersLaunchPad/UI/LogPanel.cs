using ImGuiNET;
using StationeersLaunchPad.Loading;

namespace StationeersLaunchPad.UI
{
  public static class LogPanel
  {
    private static bool standaloneLogsOpen = false;
    private static LoadedMod logFilter = null;
    public static void OpenStandaloneLogs()
    {
      standaloneLogsOpen = true;
      logFilter = null;
    }
    public static void DrawStandaloneLogs()
    {
      if (!standaloneLogsOpen)
        return;
      ImGuiHelper.Draw(() =>
      {
        ImGui.SetNextWindowSize(new(1200, 800), ImGuiCond.Appearing);
        ImGui.SetNextWindowSizeConstraints(
          new(400, 300), new(float.PositiveInfinity, float.PositiveInfinity));
        ImGui.Begin("Mod Logs",
          ref standaloneLogsOpen, ImGuiWindowFlags.NoSavedSettings);

        if (ImGui.BeginCombo("##modfilter", logFilter?.Info.Name ?? "All"))
        {
          if (ImGui.Selectable("All", logFilter == null))
            logFilter = null;
          var idx = 0;
          foreach (var mod in ModLoader.LoadedMods)
          {
            ImGui.PushID(idx);

            if (ImGui.Selectable(mod.Info.Name, logFilter == mod))
              logFilter = mod;

            ImGui.PopID();
            idx++;
          }
          ImGui.EndCombo();
        }
        DrawConsole(logFilter?.Logger ?? Logger.Global);

        ImGui.End();
      });
    }

    private static ulong lastLineCount = 0;
    private static Logger lastLogger = null;
    public static void DrawConsole(Logger logger)
    {
      ConfigPanel.DrawEnumEntry(Configs.LogSeverities, Configs.LogSeverities.Value);
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
