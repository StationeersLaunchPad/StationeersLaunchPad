
using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using ImGuiNET;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public enum DialogTone { Normal, Warn, Error }

// onClick returns false to keep the dialog open
public sealed class DialogButton(string label, Func<bool> onClick = null, bool primary = false,
  string tooltip = null, Func<bool> enabled = null, bool cancel = false)
{
  public readonly string Label = label;
  public readonly Func<bool> OnClick = onClick;
  public readonly bool Primary = primary;
  public readonly string Tooltip = tooltip;
  public readonly Func<bool> Enabled = enabled;
  // escape presses it
  public readonly bool Cancel = cancel;
}

// every SLP dialog: a solid dark panel with a thin border. plain windows, ImGui modals dim
// the screen in the game's pink
public static class SlpDialog
{
  public const float DefaultWidth = 600f;
  private static readonly Vector2 Padding = new(20f, 16f);

  private sealed class Entry(string title, Action body, DialogTone tone, float width, DialogButton[] buttons)
  {
    internal readonly string Title = title;
    internal readonly Action Body = body;
    internal readonly DialogTone Tone = tone;
    internal readonly float Width = width;
    internal readonly DialogButton[] Buttons = buttons;
    internal int Result = -1;
    internal bool Done;
  }

  private static readonly List<Entry> queue = [];
  private static readonly List<Action> panels = [];
  private static int lastFrame = -1;

  public static bool Open => queue.Count > 0;

  // panels that draw themselves with BeginPanel, like the server pack card
  public static void AddPanel(Action draw)
  {
    if (!panels.Contains(draw))
      panels.Add(draw);
  }

  // returns the index of the clicked button, or -1 when closed from code
  public static UniTask<int> Show(string title, string text, DialogTone tone, params DialogButton[] buttons) =>
    Show(title, () => ScrollText(text), tone, DefaultWidth, buttons);

  public static async UniTask<int> Show(string title, Action body, DialogTone tone, float width, params DialogButton[] buttons)
  {
    var entry = new Entry(title, body, tone, width, buttons.Length > 0 ? buttons : [new("OK", cancel: true)]);
    queue.Add(entry);
    while (!entry.Done)
      await UniTask.Yield();
    return entry.Result;
  }

  public static void CloseAll()
  {
    foreach (var entry in queue)
      entry.Done = true;
    queue.Clear();
  }

  // called from the splash, the loading screen and the game, draws once per frame
  public static void Draw()
  {
    if (Time.frameCount == lastFrame || (queue.Count == 0 && panels.Count == 0))
      return;
    lastFrame = Time.frameCount;
    ImGuiHelper.Draw(() =>
    {
      foreach (var panel in panels.ToList())
        panel();
      if (queue.Count > 0)
        DrawEntry(queue[0]);
    });
  }

  private static void DrawEntry(Entry entry)
  {
    BeginPanel("##slpdialog", ImGui.GetIO().DisplaySize * 0.5f, new Vector2(0.5f, 0.5f), entry.Width, entry.Tone, focus: true);
    if (!string.IsNullOrEmpty(entry.Title))
      Title(entry.Title);
    entry.Body?.Invoke();
    Gap();
    var clicked = ButtonRow(entry.Buttons);
    ImGui.End();
    if (clicked < 0)
      return;
    var button = entry.Buttons[clicked];
    if (button.OnClick != null && !button.OnClick())
      return;
    entry.Result = clicked;
    entry.Done = true;
    queue.Remove(entry);
  }

  public static Color BorderColor(DialogTone tone) => tone switch
  {
    DialogTone.Warn => LaunchPadTheme.Over(LaunchPadTheme.Warn, 0.6f),
    DialogTone.Error => LaunchPadTheme.Over(LaunchPadTheme.Err, 0.6f),
    _ => LaunchPadTheme.AccentBorder,
  };

  // starts a dialog window, end it with ImGui.End(). the width is capped to the screen
  public static void BeginPanel(string id, Vector2 pos, Vector2 pivot, float width,
    DialogTone tone = DialogTone.Normal, bool focus = false)
  {
    var display = ImGui.GetIO().DisplaySize;
    ImGui.SetNextWindowPos(pos, ImGuiCond.Always, pivot);
    ImGui.SetNextWindowSize(new Vector2(Math.Min(width, display.x - 48f), 0f), ImGuiCond.Always);
    if (focus)
      ImGui.SetNextWindowFocus();
    ImGui.PushStyleColor(ImGuiCol.Border, (Vector4)BorderColor(tone));
    ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Padding);
    ImGui.Begin(id, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove
      | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize);
    ImGui.PopStyleVar(2);
    ImGui.PopStyleColor();
    // the game's window background is see-through on some screens, so paint it solid
    var min = ImGui.GetWindowPos();
    ImGui.GetWindowDrawList().AddRectFilled(min, min + ImGui.GetWindowSize(),
      ImGui.ColorConvertFloat4ToU32((Vector4)new Color(LaunchPadTheme.Deep.r, LaunchPadTheme.Deep.g, LaunchPadTheme.Deep.b, 0.97f)),
      ImGui.GetStyle().WindowRounding);
  }

  public static void Title(string text) => Text(text, LaunchPadTheme.Text);

  public static void Text(string text) => Text(text, LaunchPadTheme.TextSub);

  public static void Text(string text, Color color)
  {
    ImGui.PushTextWrapPos(0f);
    ImGuiHelper.TextColored(text, color);
    ImGui.PopTextWrapPos();
  }

  // long text like release notes scrolls instead of growing past half the screen
  public static void ScrollText(string text)
  {
    var width = ImGui.GetContentRegionAvail().x;
    var height = ImGui.CalcTextSize(text, false, width).y;
    var max = ImGui.GetIO().DisplaySize.y * 0.5f;
    if (height <= max)
    {
      Text(text);
      return;
    }
    ImGui.BeginChild("##dialogtext", new Vector2(width, max));
    Text(text);
    ImGui.EndChild();
  }

  public static void Gap() => ImGui.Dummy(new Vector2(0f, ImGui.GetTextLineHeight() * 0.4f));

  // equal width buttons across the panel, returns the clicked index or -1
  public static int ButtonRow(IReadOnlyList<DialogButton> buttons)
  {
    var spacing = ImGui.GetStyle().ItemSpacing.x;
    var width = (ImGui.GetContentRegionAvail().x - spacing * (buttons.Count - 1)) / buttons.Count;
    var size = new Vector2(width, ImGui.GetFrameHeight() * 1.3f);
    var clicked = -1;
    for (var i = 0; i < buttons.Count; i++)
    {
      var button = buttons[i];
      if (i > 0)
        ImGui.SameLine();
      var enabled = button.Enabled?.Invoke() ?? true;
      var label = $"{button.Label}##dialogbutton{i}";
      bool pressed;
      if (button.Primary)
        pressed = Widgets.PrimaryButton(label, size, enabled, 1f);
      else
      {
        ImGui.BeginDisabled(!enabled);
        pressed = ImGui.Button(label, size);
        ImGui.EndDisabled();
      }
      if (!string.IsNullOrEmpty(button.Tooltip))
        ImGuiHelper.ItemTooltip(button.Tooltip);
      // the game doesn't always pass escape on to ImGui
      var escape = ImGui.IsKeyPressed(ImGuiKey.Escape, false) || Input.GetKeyDown(KeyCode.Escape);
      if (pressed || (enabled && button.Cancel && escape))
        clicked = i;
    }
    return clicked;
  }
}
