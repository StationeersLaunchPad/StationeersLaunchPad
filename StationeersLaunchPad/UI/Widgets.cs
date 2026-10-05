using System;
using System.Collections.Generic;
using ImGuiNET;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public static class Widgets
{
  private static uint U32(Color color) => ImGui.ColorConvertFloat4ToU32((Vector4)color);

  public static void PageHeader(string title, string subtitle = null)
  {
    var drawList = ImGui.GetWindowDrawList();
    ImGui.SetWindowFontScale(1.2f);
    var pos = ImGui.GetCursorScreenPos();
    ImGuiHelper.Text(title);
    drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), pos + new Vector2(1f, 0f),
      ImGui.GetColorU32(ImGuiCol.Text), title);
    ImGui.SetWindowFontScale(1f);
    if (!string.IsNullOrEmpty(subtitle))
    {
      ImGui.PushTextWrapPos(0f);
      ImGuiHelper.TextColored(subtitle, LaunchPadTheme.TextMuted);
      ImGui.PopTextWrapPos();
    }
    ImGui.Dummy(new Vector2(0f, ImGui.GetTextLineHeight() * 0.25f));
  }

  public static void SectionHeader(string title)
  {
    ImGui.Dummy(new Vector2(0f, ImGui.GetTextLineHeight() * 0.5f));
    var pos = ImGui.GetCursorScreenPos();
    var label = title.ToUpperInvariant();
    ImGuiHelper.TextColored(label, LaunchPadTheme.TextMuted);
    DrawHeaderLine(pos, label);
    ImGui.Spacing();
  }

  private static readonly Dictionary<string, bool> collapsed = [];

  // returns true when open
  public static bool CollapsibleSection(string id, string title, bool defaultOpen = true)
  {
    if (!collapsed.TryGetValue(id, out var isCollapsed))
      isCollapsed = !defaultOpen;

    ImGui.Dummy(new Vector2(0f, ImGui.GetTextLineHeight() * 0.5f));
    var pos = ImGui.GetCursorScreenPos();
    var label = title.ToUpperInvariant();
    var lineHeight = ImGui.GetTextLineHeight();
    var arrowSize = lineHeight * 0.55f;
    var width = ImGui.GetContentRegionAvail().x;

    ImGui.PushID(id);
    var clicked = ImGui.InvisibleButton("##section", new Vector2(Math.Max(1f, width), lineHeight));
    var hovered = ImGui.IsItemHovered();
    ImGui.PopID();
    if (clicked)
      collapsed[id] = isCollapsed = !isCollapsed;

    var color = hovered ? LaunchPadTheme.TextSub : LaunchPadTheme.TextMuted;
    var drawList = ImGui.GetWindowDrawList();
    var center = pos + new Vector2(arrowSize * 0.5f, lineHeight * 0.5f);
    var half = arrowSize * 0.5f;
    if (isCollapsed)
      drawList.AddTriangleFilled(center + new Vector2(-half * 0.6f, -half),
        center + new Vector2(-half * 0.6f, half), center + new Vector2(half * 0.8f, 0f), U32(color));
    else
      drawList.AddTriangleFilled(center + new Vector2(-half, -half * 0.6f),
        center + new Vector2(half, -half * 0.6f), center + new Vector2(0f, half * 0.8f), U32(color));

    var textPos = pos + new Vector2(arrowSize + ImGui.GetStyle().ItemSpacing.x, 0f);
    drawList.AddText(textPos, U32(color), label);
    DrawHeaderLine(textPos, label);
    if (!isCollapsed)
      ImGui.Spacing();
    return !isCollapsed;
  }

  private static void DrawHeaderLine(Vector2 labelPos, string label)
  {
    var size = ImGui.CalcTextSize(label);
    var lineY = labelPos.y + size.y * 0.5f;
    var lineStart = labelPos.x + size.x + ImGui.GetStyle().ItemSpacing.x;
    var lineEnd = ImGui.GetWindowPos().x + ImGui.GetWindowContentRegionMax().x;
    if (lineEnd > lineStart)
      ImGui.GetWindowDrawList().AddLine(new Vector2(lineStart, lineY), new Vector2(lineEnd, lineY),
        LaunchPadTheme.OverU32(Color.white, 0.12f));
  }

  public static void Chip(string label, Color color)
  {
    var padding = new Vector2(ImGui.GetStyle().FramePadding.x, 1f);
    var size = ImGui.CalcTextSize(label) + padding * 2f;
    var min = ImGui.GetCursorScreenPos();
    var drawList = ImGui.GetWindowDrawList();
    drawList.AddRectFilled(min, min + size, LaunchPadTheme.OverU32(color, 0.12f), 3f);
    drawList.AddRect(min, min + size, LaunchPadTheme.OverU32(color, 0.4f), 3f);
    drawList.AddText(min + padding, U32(color), label);
    ImGui.Dummy(size);
  }

  public static void Chips(IEnumerable<string> labels, Color color)
  {
    var right = ImGui.GetCursorScreenPos().x + ImGui.GetContentRegionAvail().x;
    var first = true;
    foreach (var label in labels)
    {
      var width = ImGui.CalcTextSize(label).x + ImGui.GetStyle().FramePadding.x * 2f;
      if (!first)
      {
        ImGui.SameLine();
        if (ImGui.GetCursorScreenPos().x + width > right)
          ImGui.NewLine();
      }
      Chip(label, color);
      first = false;
    }
  }

  public struct NavItem
  {
    public string Label;
    public bool Disabled;
    public string Tooltip;
  }

  // returns the clicked index, or -1
  public static int NavBar(string id, IReadOnlyList<NavItem> items, int selected)
  {
    var clicked = -1;
    var drawList = ImGui.GetWindowDrawList();
    var start = ImGui.GetCursorScreenPos();
    var padX = ImGui.GetStyle().FramePadding.x * 2f;
    var height = ImGui.GetTextLineHeight() + ImGui.GetStyle().FramePadding.y * 4f;
    var x = start.x;

    ImGui.PushID(id);
    for (var i = 0; i < items.Count; i++)
    {
      var item = items[i];
      var textSize = ImGui.CalcTextSize(item.Label);
      var size = new Vector2(textSize.x + padX * 2f, height);
      ImGui.SetCursorScreenPos(new Vector2(x, start.y));
      ImGui.PushID(i);
      var pressed = ImGui.InvisibleButton("##nav", size);
      var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
      ImGui.PopID();
      if (!string.IsNullOrEmpty(item.Tooltip) && hovered)
        ImGuiHelper.TextTooltip(item.Tooltip, 500f);
      if (pressed && !item.Disabled)
        clicked = i;

      var isSelected = i == selected;
      var color = item.Disabled ? LaunchPadTheme.TextDim
        : isSelected ? LaunchPadTheme.Text
        : hovered ? LaunchPadTheme.Text
        : LaunchPadTheme.TextSub;
      if (hovered && !item.Disabled && !isSelected)
        drawList.AddRectFilled(new Vector2(x, start.y), new Vector2(x + size.x, start.y + height),
          LaunchPadTheme.OverU32(Color.white, 0.04f));
      var textPos = new Vector2(x + padX, start.y + (height - textSize.y) / 2f);
      drawList.AddText(textPos, U32(color), item.Label);
      if (isSelected)
      {
        drawList.AddText(textPos + new Vector2(1f, 0f), U32(color), item.Label);
        drawList.AddRectFilled(new Vector2(x, start.y + height - 2f),
          new Vector2(x + size.x, start.y + height), U32(LaunchPadTheme.Accent));
      }
      x += size.x;
    }
    ImGui.PopID();

    var right = ImGui.GetWindowPos().x + ImGui.GetWindowContentRegionMax().x;
    drawList.AddLine(new Vector2(start.x, start.y + height - 0.5f), new Vector2(right, start.y + height - 0.5f),
      LaunchPadTheme.OverU32(Color.white, 0.12f));
    ImGui.SetCursorScreenPos(new Vector2(start.x, start.y + height));
    ImGui.Dummy(new Vector2(0f, ImGui.GetStyle().ItemSpacing.y));
    return clicked;
  }

  // joined toggle buttons, exactly one is selected
  public static int Segmented(string id, IReadOnlyList<string> labels, int selected)
  {
    var drawList = ImGui.GetWindowDrawList();
    var padding = ImGui.GetStyle().FramePadding;
    var height = ImGui.GetFrameHeight();
    var pos = ImGui.GetCursorScreenPos();
    var start = pos;
    ImGui.PushID(id);
    for (var i = 0; i < labels.Count; i++)
    {
      var size = new Vector2(ImGui.CalcTextSize(labels[i]).x + padding.x * 4f, height);
      ImGui.SetCursorScreenPos(pos);
      ImGui.PushID(i);
      if (ImGui.InvisibleButton("##seg", size))
        selected = i;
      var hovered = ImGui.IsItemHovered();
      ImGui.PopID();
      var fill = i == selected ? LaunchPadTheme.Over(LaunchPadTheme.Accent, 0.28f)
        : hovered ? LaunchPadTheme.Over(Color.white, 0.09f)
        : LaunchPadTheme.Over(Color.white, 0.04f);
      drawList.AddRectFilled(pos, pos + size, U32(fill));
      var text = i == selected || hovered ? LaunchPadTheme.Text : LaunchPadTheme.TextSub;
      drawList.AddText(pos + new Vector2(padding.x * 2f, padding.y), U32(text), labels[i]);
      pos.x += size.x;
    }
    ImGui.PopID();
    drawList.AddRect(start, new Vector2(pos.x, start.y + height), LaunchPadTheme.OverU32(Color.white, 0.15f));
    ImGui.SetCursorScreenPos(start);
    ImGui.Dummy(new Vector2(pos.x - start.x, height));
    return selected;
  }

  // the Discord's trusted modder checkmark, one text line tall
  public static void TrustedBadge()
  {
    var height = ImGui.GetTextLineHeight();
    var size = new Vector2(height, height);
    var min = ImGui.GetCursorScreenPos();
    ImGui.Dummy(size);
    // nudged onto the text baseline and faded next to the grey author name
    if (ModImages.TryGetBuiltIn(ModImages.TrustedImage, out var id, out _))
    {
      var offset = new Vector2(0f, Mathf.Round(height * 0.12f));
      ImGui.GetWindowDrawList().AddImage(id, min + offset, min + size + offset,
        Vector2.zero, Vector2.one, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.4f)));
    }
    ImGuiHelper.ItemTooltip(
      "Trusted modder\n\n"
      + "One or multiple author(s) named here are recognized by the Stationeers Modding Community for their work.\n\n"
      + "Only the author name field is checked. This is not a rating of the mod's quality or security.",
      ImGui.GetFontSize() * 28f);
  }

  // solid accent button for the main action on screen
  public static bool PrimaryButton(string label, Vector2 size, bool enabled, float fontScale = 1.25f)
  {
    var accent = LaunchPadTheme.Accent;
    var pos = ImGui.GetCursorScreenPos();
    var pressed = ImGui.InvisibleButton(label, size);
    var hovered = ImGui.IsItemHovered();
    var held = ImGui.IsItemActive();
    var fill = !enabled ? LaunchPadTheme.Over(accent, 0.35f)
      : held ? Color.Lerp(accent, Color.black, 0.15f)
      : hovered ? Color.Lerp(accent, Color.white, 0.15f)
      : accent;
    var drawList = ImGui.GetWindowDrawList();
    drawList.AddRectFilled(pos, pos + size, U32(fill), 3f);
    ImGui.SetWindowFontScale(fontScale);
    var text = label.Split(["##"], StringSplitOptions.None)[0];
    var textSize = ImGui.CalcTextSize(text);
    var textPos = pos + (size - textSize) / 2f;
    var textColor = enabled ? LaunchPadTheme.Deep : LaunchPadTheme.TextMuted;
    drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), textPos, U32(textColor), text);
    drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), textPos + new Vector2(1f, 0f), U32(textColor), text);
    ImGui.SetWindowFontScale(1f);
    return pressed && enabled;
  }
}
