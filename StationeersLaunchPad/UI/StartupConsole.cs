
using Assets.Scripts.Util;
using ImGuiNET;
using StationeersLaunchPad.Commands;
using System;
using System.Linq;
using Util.Commands;

namespace StationeersLaunchPad.UI
{
  public static class StartupConsole
  {
    private static string input = "";
    private static bool submitted = false;

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
        Logger.Global.LogInfo($"> {input}");
        try
        {
          res = RootCommand.StartupInstance.Execute(args);
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

      return false;
    }
  }
}