using ImGuiNET;
using System;
using System.Runtime.CompilerServices;
using UI.ImGuiUi;
using UnityEngine;

namespace StationeersLaunchPad
{
  public static class ImGuiHelper
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Text(string text)
    {
      ImGui.TextUnformatted(text);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TextColored(string text, Vector4 color)
    {
      ImGui.PushStyleColor(ImGuiCol.Text, color);
      Text(text);
      ImGui.PopStyleColor(1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TextColored(string text, Color color) => TextColored(text, (Vector4) color);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TextRight(string text, float padding = 2.0f)
    {
      var maxCorner = ImGui.GetContentRegionMax();
      var width = ImGui.CalcTextSize(text).x;
      ImGui.SetCursorPosX(maxCorner.x - padding - width);
      var minCorner = ImGui.GetCursorPos();
      ImGui.GetWindowDrawList().AddRectFilled(minCorner, maxCorner, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 1)));
      Text(text);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TextRightDisabled(string text, float padding = 2.0f) => TextRightColored(text, TextDisabledColor, padding);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TextRightColored(string text, Color color, float padding = 2.0f)
    {
      var maxCorner = ImGui.GetContentRegionMax();
      var width = ImGui.CalcTextSize(text).x;
      ImGui.SetCursorPosX(maxCorner.x - padding - width);
      var minCorner = ImGui.GetCursorPos();
      ImGui.GetWindowDrawList().AddRectFilled(minCorner, maxCorner, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 1)));
      TextColored(text, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TextTooltip(string text, float wrapWidth = float.MaxValue)
    {
      ImGui.BeginTooltip();
      ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapWidth);
      Text(text);
      ImGui.PopTextWrapPos();
      ImGui.EndTooltip();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ItemTooltip(string text, float wrapWidth = float.MaxValue, ImGuiHoveredFlags hoverFlags = ImGuiHoveredFlags.None)
    {
      if (ImGui.IsItemHovered(hoverFlags))
        TextTooltip(text, wrapWidth);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TextWrapped(string text)
    {
      ImGui.TextWrapped(text);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TextDisabled(string text)
    {
      TextColored(text, TextDisabledColor);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TextWarning(string text)
    {
      TextColored(text, Yellow);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TextError(string text)
    {
      TextColored(text, Red);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TextSuccess(string text)
    {
      TextColored(text, Green);
    }

    public static void Separator(float widthMult = 0.5f, float thickness = 1.0f, bool left = false)
    {
      var region = ImGui.GetContentRegionAvail();
      var width = region * widthMult;
      var cursor = ImGui.GetCursorPos();

      var start = cursor;
      if (left)
        start.x += region.x - width.x;
      var end = new Vector2(start.x + width.x, start.y);

      ImGui.GetWindowDrawList().AddLine(start, end, ImGui.GetColorU32(SeparatorColor), thickness);
      var spacing = ImGui.GetStyle().ItemSpacing.y;
      var lineHeight = ImGui.GetTextLineHeight() / 2.0f;

      ImGui.Dummy(new Vector2(0.0f, spacing));
      ImGui.SetCursorScreenPos(new Vector2(cursor.x, cursor.y + lineHeight));
    }

    public static void SeparatorLeft(float widthMult = 0.5f, float thickness = 1.0f)
    {
      Separator(widthMult, thickness, true);
    }

    public static void Draw(Action drawFunc)
    {
      ImGuiHelper.PushDefaultStyle();

      try
      {
        drawFunc?.Invoke();
      }
      catch (Exception ex)
      {
        Logger.Global.LogError("Failure while drawing imgui");
        Logger.Global.LogException(ex);
      }

      ImGuiHelper.PopDefaultStyle();
    }

    public static void DrawIfHovering(Action func)
    {
      if (ImGui.IsWindowHovered())
        func?.Invoke();
    }

    public static void DrawIfMouseClicked(ImGuiMouseButton button, Action func)
    {
      if (ImGui.IsMouseClicked(button))
        func?.Invoke();
    }

    public static void DrawIfMouseDoubleClicked(ImGuiMouseButton button, Action func)
    {
      if (ImGui.IsMouseDoubleClicked(button))
        func?.Invoke();
    }

    public static void DrawIfMouseDown(ImGuiMouseButton button, Action func)
    {
      if (ImGui.IsMouseDown(button))
        func?.Invoke();
    }

    public static void DrawIfMouseUp(ImGuiMouseButton button, Action func)
    {
      if (ImGui.IsMouseReleased(button))
        func?.Invoke();
    }

    public static void DrawIfDragging(ImGuiMouseButton button, Action func)
    {
      if (ImGui.IsMouseDragging(button))
        func?.Invoke();
    }

    public static void DrawIfChild(bool child, Action func, Action childFunc)
    {
      if (child)
        func?.Invoke();
      else
        childFunc?.Invoke();
    }

    public static void DrawWithPadding(Action func, float padding = 25.0f)
    {
      var screenSize = ImguiHelper.ScreenSize;
      var paddingVector = new Vector2(padding, padding);
      var topLeft = new Vector2(paddingVector.x, screenSize.y - 100.0f - paddingVector.y);
      var bottomRight = screenSize - paddingVector;

      ImGui.SetNextWindowSize(bottomRight - topLeft);
      ImGui.SetNextWindowPos(topLeft);
      func?.Invoke();
    }

    public static void DrawWithPadding2(Action func, float padding = 25.0f)
    {
      var screenSize = ImguiHelper.ScreenSize;
      var paddingVector = new Vector2(padding, padding);
      var topLeft = new Vector2(paddingVector.x, screenSize.y * 0.4f);
      var bottomRight = screenSize - paddingVector;

      ImGui.SetNextWindowSize(bottomRight - topLeft);
      ImGui.SetNextWindowPos(topLeft);
      func?.Invoke();
    }

    public static void DrawSameLine(Action func, bool nl = false)
    {
      ImGui.SameLine();
      func?.Invoke();
      if (nl)
        ImGui.SameLine();
    }

    public static bool DrawIfHovering(Func<bool> func) => ImGui.IsWindowHovered() ? func?.Invoke() ?? false : false;
    public static bool DrawIfMouseClicked(ImGuiMouseButton button, Func<bool> func) => ImGui.IsMouseClicked(button) ? func?.Invoke() ?? false : false;
    public static bool DrawIfMouseDoubleClicked(ImGuiMouseButton button, Func<bool> func) => ImGui.IsMouseDoubleClicked(button) ? func?.Invoke() ?? false : false;
    public static bool DrawIfMouseDown(ImGuiMouseButton button, Func<bool> func) => ImGui.IsMouseDown(button) ? func?.Invoke() ?? false : false;
    public static bool DrawIfMouseUp(ImGuiMouseButton button, Func<bool> func) => ImGui.IsMouseReleased(button) ? func?.Invoke() ?? false : false;
    public static bool DrawIfDragging(ImGuiMouseButton button, Func<bool> func) => ImGui.IsMouseDragging(button) ? func?.Invoke() ?? false : false;
    public static bool DrawIfChild(bool child, Func<bool> func, Func<bool> childFunc) => child ? childFunc?.Invoke() ?? false : func?.Invoke() ?? false;

    public static Color White => new Color(0.0f, 0.0f, 0.0f, 1.0f);
    public static Color Red = new Color(1.0f, 0.0f, 0.0f, 1.0f);
    public static Color Green => new Color(0.0f, 1.0f, 0.0f, 1.0f);
    public static Color Blue => new Color(0.0f, 0.0f, 1.0f, 1.0f);
    public static Color Black => new Color(1.0f, 1.0f, 1.0f, 1.0f);

    public static Color Opaque => new Color(0.0f, 0.0f, 0.0f, 1.0f);
    public static Color Translucent => new Color(0.0f, 0.0f, 0.0f, 0.5f);
    public static Color Transparent => new Color(0.0f, 0.0f, 0.0f, 0.0f);

    public static Color Yellow = new Color(0.7f, 0.7f, 0.0f, 1.0f);

    public static Color TextColor => _colors[(int) ImGuiCol.Text];
    public static Color TextDisabledColor => _colors[(int) ImGuiCol.TextDisabled];
    public static Color SeparatorColor => _colors[(int) ImGuiCol.Separator];

    private static Color[] _colors;
    public static void PushDefaultStyle()
    {
      if (_colors == null)
      {
        _colors = new Color[(int) ImGuiCol.COUNT];
        for (var i = 0; i < (int) ImGuiCol.COUNT; i++)
        {
          var c = ImGui.ColorConvertU32ToFloat4(ImGui.GetColorU32((ImGuiCol) i));

          _colors[i] = new Color(c.x, c.y, c.z, c.w);
          _colors[i] = _colors[i] * _colors[i][3];
          if (_colors[i][3] != 0f)
            _colors[i][3] = 1f;
        }
      }

      for (var i = (ImGuiCol) 0; i < ImGuiCol.COUNT; i++)
      {
        ImGui.PushStyleColor(i, _colors[(int) i]);
      }

      ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(3, 2));
      ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(3, 2));
      ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 3);
      ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 1.0f);
    }

    public static void PopDefaultStyle()
    {
      ImGui.PopStyleVar(4);
      ImGui.PopStyleColor((int) ImGuiCol.COUNT);
    }
  }
}