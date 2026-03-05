
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
    private static bool refocus = false;
    private static CircularBuffer<string> commandHistory = new(64);
    private static int historyIndex = -1;

    public unsafe static bool DrawInput(Rect rect)
    {
      ImGui.SetCursorScreenPos(rect.TL);
      ImGuiHelper.TextDisabled(">");
      ImGui.SameLine();

      rect.SplitAX(ImGui.GetCursorScreenPos().x, out _, out rect);

      var flags = ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CallbackAlways;
      if (refocus)
        flags |= ImGuiInputTextFlags.ReadOnly;
      if (ImGui.InputTextWithHint("##console", "Enter Command", ref input, 1024, flags, static data =>
      {
        if (refocus)
          data->CursorPos = data->SelectionStart = data->SelectionEnd = data->BufTextLen;
        return 0;
      }))
      {
        var args = CmdLineParser.SplitCommandLine(input).ToArray();
        if (args.Length > 0 && args[0].Equals("slp", StringComparison.OrdinalIgnoreCase))
          args = args[1..];
        string res = null;
        Logger.Global.LogInfo($"> {input}");
        try
        {
          res = SLPCommand.RunCommand(args);
        }
        catch (Exception ex)
        {
          Logger.Global.LogException(ex);
        }
        if (res != null)
          Logger.Global.LogInfo(res);
        if (input.Length > 0)
          commandHistory.Add(input);
        input = "";
        refocus = true;
      }
      else if (refocus)
      {
        if (!ImGui.IsItemActive())
          ImGui.SetKeyboardFocusHere(-1);
        refocus = false;
      }

      if (ImGui.IsItemActive() && commandHistory.Length > 0)
      {
        var loadHistory = false;
        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
        {
          historyIndex++;
          loadHistory = true;
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
        {
          historyIndex--;
          loadHistory = true;
        }

        historyIndex = Math.Clamp(historyIndex, 0, commandHistory.Length - 1);
        if (loadHistory)
        {
          input = commandHistory[commandHistory.Length - historyIndex - 1];
          refocus = true;
        }
        else if (input != commandHistory[commandHistory.Length - historyIndex - 1])
          historyIndex = -1;
      }
      else
        historyIndex = -1;

      return false;
    }
  }

  public class CircularBuffer<T>
  {
    private readonly T[] values;
    private int length = 0;
    private int start = 0;

    public CircularBuffer(int capacity) => values = new T[capacity];
    public int Length => length;
    public int Capacity => values.Length;

    public T this[int index] => values[(start + index) % Capacity];

    public void Add(T val)
    {
      values[(start + length) % Capacity] = val;
      if (length == values.Length)
        start = (start + 1) % Capacity;
      else
        length++;
    }

    public void Clear() => length = start = 0;
  }
}