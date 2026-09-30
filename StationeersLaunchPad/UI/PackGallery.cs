using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using StationeersLaunchPad.Metadata;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public static class PackGallery
{
  private static ModList indexedModList;
  private static IReadOnlyDictionary<string, ModInfo> modIndex;

  public static float CardWidth(float height)
  {
    var (imageHeight, _) = Metrics(height);
    return Mathf.Max(110f, Mathf.Round(imageHeight * 16f / 9f));
  }

  private static (float imageHeight, float textHeight) Metrics(float height)
  {
    var textHeight = ImGui.GetTextLineHeight() * 2f + ImGui.GetStyle().ItemSpacing.y + 10f;
    return (Mathf.Max(20f, height - textHeight), textHeight);
  }

  // returns the clicked pack if it isn't the active one
  public static ProfileData Draw(string id, ProfileManager manager, ModList modList, Vector2 size,
    bool newCard, out bool newClicked, bool enabled = true)
  {
    newClicked = false;
    if (!ReferenceEquals(indexedModList, modList))
    {
      indexedModList = modList;
      modIndex = ProfileManager.BuildModIndex(modList.AllMods);
    }

    ProfileData picked = null;
    var style = ImGui.GetStyle();
    var cardHeight = Math.Max(1f, size.y - style.ScrollbarSize - 2f);
    var cardWidth = CardWidth(cardHeight);
    var active = manager.ActiveProfile;

    ImGui.BeginChild(id, size, false, ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
    // the wheel scrolls sideways
    if (ImGui.IsWindowHovered() && ImGui.GetIO().MouseWheel != 0f)
      ImGui.SetScrollX(Mathf.Clamp(ImGui.GetScrollX() - ImGui.GetIO().MouseWheel * (cardWidth + style.ItemSpacing.x),
        0f, ImGui.GetScrollMaxX()));
    ImGui.BeginDisabled(!enabled);

    var first = true;
    void Gap(bool divider)
    {
      if (first)
      {
        first = false;
        return;
      }
      ImGui.SameLine();
      if (!divider)
        return;
      var pos = ImGui.GetCursorScreenPos();
      ImGui.GetWindowDrawList().AddLine(new Vector2(pos.x + 2f, pos.y + 6f), new Vector2(pos.x + 2f, pos.y + cardHeight - 6f),
        LaunchPadTheme.OverU32(Color.white, 0.15f));
      ImGui.Dummy(new Vector2(4f, cardHeight));
      ImGui.SameLine();
    }

    foreach (var pack in manager.ShownBuiltInPacks)
    {
      Gap(false);
      if (DrawCard(manager, modList, pack, active, cardWidth, cardHeight))
        picked = pack;
    }
    var divide = true;
    foreach (var pack in manager.UserPacks)
    {
      Gap(divide);
      divide = false;
      if (DrawCard(manager, modList, pack, active, cardWidth, cardHeight))
        picked = pack;
    }
    divide = true;
    foreach (var pack in manager.ServerPacks)
    {
      Gap(divide);
      divide = false;
      if (DrawCard(manager, modList, pack, active, cardWidth, cardHeight))
        picked = pack;
    }
    if (newCard)
    {
      Gap(true);
      newClicked = DrawNewCard(cardWidth * 0.6f, cardHeight);
    }

    ImGui.EndDisabled();
    ImGui.EndChild();
    return picked == active ? null : picked;
  }

  private static bool DrawCard(ProfileManager manager, ModList modList, ProfileData pack, ProfileData active,
    float width, float height)
  {
    var style = ImGui.GetStyle();
    var (imageHeight, _) = Metrics(height);
    var lineHeight = ImGui.GetTextLineHeight();
    var drawList = ImGui.GetWindowDrawList();

    ImGui.PushID(pack.Name);
    var min = ImGui.GetCursorScreenPos();
    var max = min + new Vector2(width, height);
    var clicked = ImGui.InvisibleButton("##card", new Vector2(width, height));
    var hovered = ImGui.IsItemHovered();
    var isActive = pack == active;
    var isServer = ProfileManager.IsServerPack(pack);
    var missing = ProfileManager.GetMissingMods(pack, modList).Count;

    drawList.AddRectFilled(min, max, LaunchPadTheme.OverU32(Color.white, hovered ? 0.07f : 0.035f), 4f);
    var mods = manager.IsVanilla(pack) ? []
      : pack.Name == ProfileManager.VanillaPlusName ? ProfileManager.InstalledMods(manager.AlwaysOn, modIndex)
      : ProfileManager.InstalledMods(pack, modIndex);
    var imageMin = min + new Vector2(1f, 1f);
    var imageMax = new Vector2(max.x - 1f, min.y + imageHeight);
    var builtInImage = manager.IsVanilla(pack) ? ModImages.VanillaImage
      : pack.Name == ProfileManager.VanillaPlusName ? ModImages.VanillaPlusImage
      : null;
    if (builtInImage == null || !ModImages.DrawBuiltIn(drawList, builtInImage, imageMin, imageMax))
      DrawMosaic(drawList, mods, imageMin, imageMax, manager.IsVanilla(pack) ? "No mods" : "Empty");
    if (isActive)
      drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32((Vector4)LaunchPadTheme.Accent), 4f, ImDrawFlags.None, 2f);
    else
      drawList.AddRect(min, max, LaunchPadTheme.OverU32(Color.white, hovered ? 0.3f : 0.1f), 4f);

    var textPos = new Vector2(min.x + 6f, min.y + imageHeight + 5f);
    drawList.PushClipRect(textPos, new Vector2(max.x - 6f, max.y), true);
    drawList.AddText(textPos,
      ImGui.ColorConvertFloat4ToU32((Vector4)(isActive ? LaunchPadTheme.Accent : LaunchPadTheme.Text)), pack.Name);
    var count = ProfileManager.ModCount(pack);
    var detail = manager.IsVanilla(pack) ? "no mods"
      : pack.Name == ProfileManager.VanillaPlusName ? $"{ProfileManager.ModCount(manager.AlwaysOn)} always on"
      : isServer ? $"server, {count} mods"
      : $"{count} mod{(count == 1 ? "" : "s")}";
    var detailPos = textPos + new Vector2(0f, lineHeight + style.ItemSpacing.y);
    drawList.AddText(detailPos, ImGui.ColorConvertFloat4ToU32((Vector4)LaunchPadTheme.TextMuted), detail);
    var right = missing > 0 ? $"{missing} missing" : isActive ? "active" : "";
    if (right.Length > 0)
      drawList.AddText(new Vector2(max.x - 6f - ImGui.CalcTextSize(right).x, detailPos.y),
        ImGui.ColorConvertFloat4ToU32((Vector4)(missing > 0 ? LaunchPadTheme.Err : LaunchPadTheme.Accent)), right);
    drawList.PopClipRect();

    if (hovered)
      ImGuiHelper.TextTooltip(
        manager.IsVanilla(pack) ? "Vanilla: the game without any mods, not even always-on ones."
        : pack.Name == ProfileManager.VanillaPlusName ? "Vanilla+: the game with only your always-on mods."
        : isServer ? $"{pack.Name}: the mods of {pack.ServerName}, plus your always-on mods."
        : isActive ? $"{pack.Name} is the active pack." : $"Switch to {pack.Name}.",
        400f);
    ImGui.PopID();
    return clicked;
  }

  private static bool DrawNewCard(float width, float height)
  {
    var min = ImGui.GetCursorScreenPos();
    var max = min + new Vector2(width, height);
    var clicked = ImGui.InvisibleButton("##newcard", new Vector2(width, height));
    var hovered = ImGui.IsItemHovered();
    var drawList = ImGui.GetWindowDrawList();
    drawList.AddRect(min, max, LaunchPadTheme.OverU32(Color.white, hovered ? 0.35f : 0.15f), 4f);
    var color = ImGui.ColorConvertFloat4ToU32((Vector4)(hovered ? LaunchPadTheme.Text : LaunchPadTheme.TextSub));
    var center = (min + max) / 2f;
    var arm = Math.Min(width, height) * 0.12f;
    drawList.AddLine(center + new Vector2(-arm, -arm * 0.6f), center + new Vector2(arm, -arm * 0.6f), color, 2f);
    drawList.AddLine(center + new Vector2(0f, -arm * 1.6f), center + new Vector2(0f, arm * 0.4f), color, 2f);
    var label = "New pack";
    drawList.AddText(new Vector2(center.x - ImGui.CalcTextSize(label).x / 2f, center.y + arm), color, label);
    if (hovered)
      ImGuiHelper.TextTooltip("Start a new pack.");
    return clicked;
  }

  public static void DrawMosaic(ImDrawListPtr drawList, List<ModInfo> mods, Vector2 min, Vector2 max, string emptyText)
  {
    if (mods.Count == 0)
    {
      drawList.AddRectFilled(min, max, LaunchPadTheme.OverU32(Color.black, 0.3f), 4f, ImDrawFlags.RoundCornersTop);
      var size = ImGui.CalcTextSize(emptyText);
      drawList.AddText((min + max - size) / 2f, ImGui.ColorConvertFloat4ToU32((Vector4)LaunchPadTheme.TextMuted), emptyText);
      return;
    }
    var shown = mods.Take(4).ToList();
    var mid = (min + max) / 2f;
    switch (shown.Count)
    {
      case 1:
        ModImages.DrawFill(drawList, shown[0], min, max);
        break;
      case 2:
        ModImages.DrawFill(drawList, shown[0], min, new Vector2(mid.x - 1f, max.y));
        ModImages.DrawFill(drawList, shown[1], new Vector2(mid.x + 1f, min.y), max);
        break;
      case 3:
        ModImages.DrawFill(drawList, shown[0], min, new Vector2(mid.x - 1f, max.y));
        ModImages.DrawFill(drawList, shown[1], new Vector2(mid.x + 1f, min.y), new Vector2(max.x, mid.y - 1f));
        ModImages.DrawFill(drawList, shown[2], new Vector2(mid.x + 1f, mid.y + 1f), max);
        break;
      default:
        ModImages.DrawFill(drawList, shown[0], min, new Vector2(mid.x - 1f, mid.y - 1f));
        ModImages.DrawFill(drawList, shown[1], new Vector2(mid.x + 1f, min.y), new Vector2(max.x, mid.y - 1f));
        ModImages.DrawFill(drawList, shown[2], new Vector2(min.x, mid.y + 1f), new Vector2(mid.x - 1f, max.y));
        ModImages.DrawFill(drawList, shown[3], new Vector2(mid.x + 1f, mid.y + 1f), max);
        break;
    }
  }
}
