
using Assets.Scripts.Util;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Util.Commands;

namespace StationeersLaunchPad.UI
{
  public static class StartupConsole
  {
    private static string input = "";
    private static bool submitted = false;
    private static string helpText = null;
    private static string helpInput = null;

    public static bool DrawInput(Rect rect)
    {
      ImGui.SetCursorScreenPos(rect.TL);
      ImGuiHelper.TextDisabled(">");
      ImGui.SameLine();

      rect.SplitAX(ImGui.GetCursorScreenPos().x, out _, out rect);
      ImGui.SetNextItemWidth(rect.Size.x);
      if (ImGui.InputTextWithHint(
        "##console", "Enter Command", ref input, 1024, ImGuiInputTextFlags.EnterReturnsTrue))
      {
        submitted = true;
        var args = CmdLineParser.SplitCommandLine(input).ToArray();
        string res = null;
        try
        {
          res = SLPCommand.Instance.ExecuteStartup(args);
        }
        catch (Exception ex)
        {
          Logger.Global.LogException(ex);
        }
        if (res != null)
          Logger.Global.LogInfo(res);
        input = "";
      }
      else if (submitted)
      {
        ImGui.SetKeyboardFocusHere(-1);
        submitted = false;
      }

      if (ImGui.IsItemActive() && input.Length > 0)
        DrawPopupWindow(rect);

      return false;
    }

    private static void DrawPopupWindow(Rect inputRect)
    {
      BuildHelpText();

      var style = ImGui.GetStyle();

      var textSz = ImGui.CalcTextSize(helpText);

      var popupHeight = style.FramePadding.y * 2 + textSz.y;

      var xoffset = style.WindowPadding.x - style.FramePadding.x;
      ImGui.SetNextWindowPos(inputRect.TL - new Vector2(xoffset, popupHeight));
      ImGui.SetNextWindowSizeConstraints(
        new(200, popupHeight),
        new(inputRect.Size.x + style.WindowPadding.x, popupHeight));
      ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
      ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(style.WindowPadding.x, 0));
      ImGui.Begin("##console_help", 0
        | ImGuiWindowFlags.NoDecoration
        | ImGuiWindowFlags.NoFocusOnAppearing
        | ImGuiWindowFlags.NoNav
        | ImGuiWindowFlags.NoInputs
        | ImGuiWindowFlags.NoSavedSettings
        | ImGuiWindowFlags.AlwaysAutoResize
      );
      ImGui.BringWindowToDisplayFront(ImGui.GetCurrentWindowRead());
      ImGuiHelper.Text(helpText);
      ImGui.End();
      ImGui.PopStyleVar(2);
    }

    private static void BuildHelpText()
    {
      if (helpText != null && ReferenceEquals(input, helpInput))
        return;
      // this only runs when the user types a character, so allocations are fine
      var sb = new StringBuilder();
      var matching = new List<SLPCmd>();

      foreach (var cmd in SLPCmd.AllCommands)
      {
        if (cmd.IsStartup && CommandMatch(cmd.Name))
          matching.Add(cmd);
      }
      if (matching.Count == 0)
      {
        foreach (var cmd in SLPCmd.AllCommands)
          if (cmd.IsStartup)
            matching.Add(cmd);
      }

      foreach (var cmd in matching)
        sb.AppendLine(cmd.StartupDescLine);

      if (matching.Count == 1 && matching[0].ExtendedDescription is string exdesc)
        sb.AppendLine(exdesc);

      helpText = sb.ToString().TrimEnd();
      helpInput = input;
    }

    private static bool CommandMatch(string name)
    {
      var inIdx = 0;
      var nameIdx = 0;

      while (inIdx < input.Length && nameIdx < name.Length)
      {
        var ci = input[inIdx];
        if (char.IsWhiteSpace(ci))
          return true;
        ci = char.ToLowerInvariant(ci);
        while (nameIdx < name.Length && ci != name[nameIdx])
          nameIdx++;
        if (nameIdx == name.Length)
          return false;
        inIdx++;
        nameIdx++;
      }
      return inIdx >= input.Length || char.IsWhiteSpace(input[inIdx]);
    }
  }
}