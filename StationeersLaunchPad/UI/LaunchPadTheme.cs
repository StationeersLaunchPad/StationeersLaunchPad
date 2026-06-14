using ImGuiNET;
using UnityEngine;

namespace StationeersLaunchPad.UI;

// Color palette + ImGui style matching the redesigned LaunchPad UI (dark panels, orange accent).
public static class LaunchPadTheme
{
  public static Color Hex(uint rgb, float a = 1f) =>
    new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

  public static readonly Color Bg = Hex(0x1E2027);
  public static readonly Color Panel = Hex(0x2B2F3B);
  public static readonly Color PanelAlt = Hex(0x252833);
  public static readonly Color Deep = Hex(0x14161C);
  public static readonly Color Sidebar = Hex(0x191C23);
  public static readonly Color Toolbar = Hex(0x181B22);
  public static readonly Color Border = new(1f, 1f, 1f, 0.07f);
  public static readonly Color RowAlt = new(1f, 1f, 1f, 0.025f);

  public static readonly Color Orange = Hex(0xF47A2A);
  public static readonly Color OrangeFaint = Hex(0xF47A2A, 0.10f);
  public static readonly Color OrangeBorder = Hex(0xF47A2A, 0.25f);

  public static readonly Color Text = Hex(0xF1F3F6);
  public static readonly Color TextSub = Hex(0xB7BDC9);
  public static readonly Color TextMuted = Hex(0x6A728A);
  public static readonly Color TextDim = Hex(0x4A5266);

  public static readonly Color Ok = Hex(0x5FAF5F);
  public static readonly Color Warn = Hex(0xD8B14A);
  public static readonly Color Err = Hex(0xD9534F);

  private static int colors;
  private static int vars;

  public static void Push()
  {
    colors = 0;
    vars = 0;

    void C(ImGuiCol c, Color col) { ImGui.PushStyleColor(c, (Vector4)col); colors++; }
    void V1(ImGuiStyleVar v, float f) { ImGui.PushStyleVar(v, f); vars++; }
    void V2(ImGuiStyleVar v, Vector2 f) { ImGui.PushStyleVar(v, f); vars++; }

    C(ImGuiCol.WindowBg, Bg);
    C(ImGuiCol.ChildBg, new Color(0f, 0f, 0f, 0f));
    C(ImGuiCol.PopupBg, Panel);
    C(ImGuiCol.Border, Border);
    C(ImGuiCol.Text, Text);
    C(ImGuiCol.TextDisabled, TextMuted);
    C(ImGuiCol.FrameBg, Deep);
    C(ImGuiCol.FrameBgHovered, PanelAlt);
    C(ImGuiCol.FrameBgActive, PanelAlt);
    C(ImGuiCol.Button, new Color(1f, 1f, 1f, 0.04f));
    C(ImGuiCol.ButtonHovered, new Color(1f, 1f, 1f, 0.09f));
    C(ImGuiCol.ButtonActive, OrangeFaint);
    C(ImGuiCol.Header, Hex(0xF47A2A, 0.16f));
    C(ImGuiCol.HeaderHovered, new Color(1f, 1f, 1f, 0.09f));
    C(ImGuiCol.HeaderActive, Hex(0xF47A2A, 0.22f));
    C(ImGuiCol.CheckMark, Orange);
    C(ImGuiCol.Separator, Border);
    C(ImGuiCol.ScrollbarBg, new Color(0f, 0f, 0f, 0f));
    C(ImGuiCol.ScrollbarGrab, new Color(1f, 1f, 1f, 0.10f));
    C(ImGuiCol.ScrollbarGrabHovered, new Color(1f, 1f, 1f, 0.18f));

    V1(ImGuiStyleVar.FrameRounding, 3f);
    V1(ImGuiStyleVar.ChildRounding, 3f);
    V1(ImGuiStyleVar.FrameBorderSize, 1f);
    V2(ImGuiStyleVar.ItemSpacing, new Vector2(6f, 5f));
  }

  public static void Pop()
  {
    ImGui.PopStyleVar(vars);
    ImGui.PopStyleColor(colors);
  }

  // -- Draw helpers ----------------------------------------------
  public static void Fill(Rect r, Color c) =>
    ImGui.GetWindowDrawList().AddRectFilled(r.Min, r.Max, U32(c));

  public static void Stroke(Rect r, Color c) =>
    ImGui.GetWindowDrawList().AddRect(r.Min, r.Max, U32(c));

  public static void HLine(Vector2 a, Vector2 b, Color c) =>
    ImGui.GetWindowDrawList().AddLine(a, b, U32(c));

  public static void TextAt(Vector2 pos, string text, Color c)
  {
    ImGui.SetCursorScreenPos(pos);
    ImGuiHelper.TextColored(text, c);
  }

  private static uint U32(Color c) => ImGui.ColorConvertFloat4ToU32((Vector4)c);
}
