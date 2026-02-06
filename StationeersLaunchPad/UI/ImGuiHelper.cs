using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace StationeersLaunchPad.UI
{
  public static class ImGuiHelper
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Text(string text)
    {
      ImGui.TextUnformatted(text);
    }

    public static void Header(string text)
    {
      PushFontScale(1.25f);
      ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 10));
      ImGui.Separator();
      ImGui.Indent();
      ImGui.TextColored(new Vector4(1.00f, 0.40f, 0.09f, 1.00f), text);
      ImGui.Separator();
      ImGui.PopStyleVar();
      PopFontScale();
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
      ImGui.GetWindowDrawList().AddRectFilled(minCorner, maxCorner, ImGui.ColorConvertFloat4ToU32(Transparent));
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
      ImGui.GetWindowDrawList().AddRectFilled(minCorner, maxCorner, ImGui.ColorConvertFloat4ToU32(Transparent));
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
    public static void TextDisabled(string text, bool disabled = true)
    {
      TextColored(text, disabled ? TextDisabledColor : TextColor);
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

    public static Vector4 FlashColor(ImGuiCol from, ImGuiCol to)
    {
      var style = ImGui.GetStyle();
      var flashPos = Mathf.Sin(Time.realtimeSinceStartup * 5f) * 0.5f + 0.5f;
      var crFrom = style.Colors[(int) from];
      var crTo = style.Colors[(int) to];
      return Vector4.Lerp(crFrom, crTo, flashPos);
    }

    public static Rect ScreenRect() => new(Vector2.zero, ImGui.GetMainViewport().Size);
    public static Rect AvailableRect()
    {
      var min = ImGui.GetCursorScreenPos();
      var size = ImGui.GetContentRegionAvail();
      return new(min, min + size);
    }

    public static void SetNextWindowRect(Rect rect)
    {
      ImGui.SetNextWindowPos(rect.Min);
      ImGui.SetNextWindowSize(rect.Size);
    }

    public static void SeparatorLine(Vector2 from, Vector2 to)
    {
      ImGui.GetWindowDrawList().AddLine(from, to,
        ImGui.ColorConvertFloat4ToU32(
          ImGui.GetStyle().Colors[(int) ImGuiCol.Separator]));
    }

    public static void Text(Rect rect, string text)
    {
      var drawList = ImGui.GetWindowDrawList();
      drawList.PushClipRect(rect.Min, rect.Max, true);

      ImGui.SetCursorScreenPos(rect.TL);
      Text(text);
      ImGui.SetCursorScreenPos(rect.TL);
      ImGui.Dummy(rect.Size);

      drawList.PopClipRect();
    }

    public static void TextCentered(Rect rect, string text)
    {
      var drawList = ImGui.GetWindowDrawList();
      drawList.PushClipRect(rect.Min, rect.Max, true);

      var sz = ImGui.CalcTextSize(text);
      var offset = (rect.Size.x - sz.x) / 2f;
      ImGui.SetCursorScreenPos(rect.TL + new Vector2(offset, 0));
      Text(text);
      ImGui.SetCursorScreenPos(rect.TL);
      ImGui.Dummy(rect.Size);

      drawList.PopClipRect();
    }

    public static void ApplyStationeersStyle()
    {
      ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 1.0f);
      ImGui.PushStyleVar(ImGuiStyleVar.DisabledAlpha, 1.0f);
      ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
      ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 11);
      ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1);
      ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(32, 32));
      ImGui.PushStyleVar(ImGuiStyleVar.WindowTitleAlign, new Vector2(0.5f, 0.5f));
      ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4);
      ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1);
      ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 4);
      ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1);
      ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 4));
      ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4);
      ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0);
      ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 4));
      ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(4, 4));
      ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4, 4));
      ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, 20);
      ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 11);
      ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 4);
      ImGui.PushStyleVar(ImGuiStyleVar.GrabMinSize, 20);
      ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 4);
      ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 4);
      ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
      ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0, 0));
      ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.00f, 1.00f, 1.00f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.TextDisabled, new Vector4(0.50f, 0.50f, 0.50f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.184f, 0.184f, 0.216f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.184f, 0.184f, 0.216f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.08f, 0.08f, 0.08f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.43f, 0.43f, 0.50f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.BorderShadow, new Vector4(0.00f, 0.00f, 0.00f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.13f, 0.13f, 0.16f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.34f, 0.34f, 0.35f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.44f, 0.25f, 0.24f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.16f, 0.16f, 0.20f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.44f, 0.25f, 0.24f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, new Vector4(0.00f, 0.00f, 0.00f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.MenuBarBg, new Vector4(0.14f, 0.14f, 0.14f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.02f, 0.02f, 0.02f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(1.00f, 1.00f, 1.00f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(1.00f, 1.00f, 1.00f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(0.78f, 0.78f, 0.78f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(1.00f, 0.40f, 0.09f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.SliderGrab, new Vector4(1.00f, 0.40f, 0.09f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, new Vector4(0.44f, 0.25f, 0.24f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.11f, 0.11f, 0.13f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.34f, 0.34f, 0.35f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.44f, 0.25f, 0.24f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.11f, 0.11f, 0.13f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.34f, 0.34f, 0.35f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.44f, 0.25f, 0.24f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.43f, 0.43f, 0.50f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, new Vector4(0.10f, 0.40f, 0.75f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.SeparatorActive, new Vector4(0.10f, 0.40f, 0.75f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.ResizeGrip, new Vector4(1.00f, 1.00f, 1.00f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, new Vector4(1.00f, 1.00f, 1.00f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, new Vector4(0.78f, 0.78f, 0.78f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(0.11f, 0.11f, 0.13f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(0.34f, 0.34f, 0.35f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.TabActive, new Vector4(1.00f, 0.40f, 0.09f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.TabUnfocused, new Vector4(0.07f, 0.10f, 0.15f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.TabUnfocusedActive, new Vector4(0.44f, 0.25f, 0.24f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.PlotLines, new Vector4(0.61f, 0.61f, 0.61f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.PlotLinesHovered, new Vector4(1.00f, 0.43f, 0.35f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.90f, 0.70f, 0.00f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.PlotHistogramHovered, new Vector4(1.00f, 0.60f, 0.00f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, new Vector4(0.19f, 0.19f, 0.20f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, new Vector4(0.31f, 0.31f, 0.35f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.TableBorderLight, new Vector4(0.23f, 0.23f, 0.25f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.TableRowBg, new Vector4(0.00f, 0.00f, 0.00f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(1.00f, 1.00f, 1.00f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, new Vector4(0.26f, 0.59f, 0.98f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.DragDropTarget, new Vector4(1.00f, 1.00f, 0.00f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.NavHighlight, new Vector4(0.26f, 0.59f, 0.98f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.NavWindowingHighlight, new Vector4(1.00f, 1.00f, 1.00f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.NavWindowingDimBg, new Vector4(0.80f, 0.80f, 0.80f, 1.00f));
      ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, new Vector4(0.80f, 0.80f, 0.80f, 1.00f));
    }
    public static void RestoreDefaultSTyle()
    {
      ImGui.PopStyleVar(25);
      ImGui.PopStyleColor(53);
    }

    static Stack<float> fontSizeStack = new();
    public static void PushFontScale(float scale)
    {
      var font = ImGui.GetFont();
      // Font.Scale doesn't work in-place and applies scaling universally, so we scale the FontSize.
      // And also, larger font size values are actually smaller display size for some reason.
      fontSizeStack.Push(font.FontSize);
      font.FontSize /= scale;
    }
    public static void PopFontScale()
    {
      var font = ImGui.GetFont();
      font.FontSize = fontSizeStack.Pop();
    }
  }

  public readonly struct Rect
  {
    public readonly Vector2 Min;
    public readonly Vector2 Max;
    public Vector2 Size => Max - Min;
    public Rect(Vector2 min, Vector2 max) =>
      (Min, Max) = (min, Vector2.Max(min, max));

    public Vector2 TL => Min;
    public Vector2 TR => new(Max.x, Min.y);
    public Vector2 BL => new(Min.x, Max.y);
    public Vector2 BR => Max;

    public float L => Min.x;
    public float T => Min.y;
    public float R => Max.x;
    public float B => Max.y;

    // split at absolute X/Y value
    public void SplitAX(float x, out Rect left, out Rect right) =>
      (left, right) = (new(Min, new(x, Max.y)), new(new(x, Min.y), Max));
    public void SplitAY(float y, out Rect top, out Rect bottom) =>
      (top, bottom) = (new(Min, new(Max.x, y)), new(new(Min.x, y), Max));

    // split at offset from left/top or negative from right/bottom
    public void SplitOX(float ox, out Rect left, out Rect right) =>
      SplitAX(ox < 0 ? Max.x + ox : Min.x + ox, out left, out right);
    public void SplitOY(float oy, out Rect top, out Rect bottom) =>
      SplitAY(oy < 0 ? Max.y + oy : Min.y + oy, out top, out bottom);

    // split where left/top is relative width/height r (0-1)
    public void SplitRX(float rx, out Rect left, out Rect right) =>
      SplitAX(Min.x * (1 - rx) + Max.x * rx, out left, out right);
    public void SplitRY(float ry, out Rect top, out Rect bottom) =>
      SplitAY(Min.y * (1 - ry) + Max.y * ry, out top, out bottom);

    public void Columns(float w0, out Rect c0, float w1, out Rect c1, out Rect c2)
    {
      SplitOX(w0, out c0, out var rest);
      rest.SplitOX(w1, out c1, out c2);
    }

    public TableRow TableRow(float rowHei, Span<float> widths) =>
      new(this.Shrink(0, 0, 0, this.Size.y - rowHei), widths);

    public Rect Shrink(float margin) => Shrink(margin, margin, margin, margin);
    public Rect Shrink(float minX, float minY, float maxX, float maxY) =>
      new(new(Min.x + minX, Min.y + minY), new(Max.x - maxX, Max.y - maxY));

    public Rect Shift(float x, float y) =>
      new(Min + new Vector2(x, y), Max + new Vector2(x, y));

    public Rect From(Vector2 min) => new(min, Max);

    public override string ToString() => $"{Min},{Max}";
  }

  public ref struct TableRow
  {
    private Rect rect;
    private Span<float> columns;
    private int lastColumn;
    private float lastColumnOffset;

    public TableRow(Rect rect, Span<float> columns)
    {
      this.rect = rect;
      this.columns = columns;
      lastColumn = 0;
      lastColumnOffset = 0;
    }

    public void NextRow() => rect = rect.Shift(0, rect.Size.y);
    public Rect Rect => rect;

    public Rect Column(int index)
    {
      if (index < lastColumn)
      {
        lastColumn = 0;
        lastColumnOffset = 0;
      }
      while (lastColumn < index)
      {
        lastColumnOffset += columns[lastColumn];
        lastColumn++;
      }
      var start = new Vector2(rect.Min.x + lastColumnOffset, rect.Min.y);
      var end = index < columns.Length
        ? new Vector2(start.x + columns[index], rect.Max.y)
        : rect.Max;
      return new(start, end);
    }

    public Rect ColumnsFrom(int startIndex)
    {
      if (startIndex < lastColumn)
      {
        lastColumn = 0;
        lastColumnOffset = 0;
      }
      while (lastColumn < startIndex)
      {
        lastColumnOffset += columns[lastColumn];
        lastColumn++;
      }
      var start = new Vector2(rect.Min.x + lastColumnOffset, rect.Min.y);
      return new(start, rect.Max);
    }
  }
}