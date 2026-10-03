
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts;
using Cysharp.Threading.Tasks;
using ImGuiNET;
using StationeersLaunchPad.Metadata;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public enum Slp2SaveChoice { None, Save, SaveAndActivate }

public static class Slp2SaveProfilePanel
{
  private const int MaxListedMods = 6;
  private const float SecondsAfterJoin = 30f;

  private sealed class OfferState(string name, List<Slp2PackageCode.Entry> mods, float completedAt)
  {
    internal readonly string Name = name;
    internal readonly List<Slp2PackageCode.Entry> Mods = mods;
    internal float CompletedAt = completedAt;
    internal bool Completed;
    internal Slp2SaveChoice Result;
  }

  private static OfferState currentOffer;
  private static float joinCompletedAt = -1f;

  private static readonly DialogButton[] buttons =
  [
    new("Save server pack", () => Choose(Slp2SaveChoice.Save),
      tooltip: "Saves the pack, you can pick it at the next start."),
    new("Save & Activate", () => Choose(Slp2SaveChoice.SaveAndActivate), primary: true,
      tooltip: "Saves the pack and makes it the active one, it loads at the next start."),
    new("No thanks", () => Choose(Slp2SaveChoice.None), cancel: true),
  ];

  // the offer stays up while loading and for a while after, it takes a moment to read
  public static void OnJoinFinished()
  {
    joinCompletedAt = Time.realtimeSinceStartup;
    if (currentOffer is { } offer)
      offer.CompletedAt = joinCompletedAt;
  }

  public static async UniTask<Slp2SaveChoice> Offer(string forServerName, List<Slp2PackageCode.Entry> mods)
  {
    if (Platform.IsServer || currentOffer != null)
      return Slp2SaveChoice.None;
    if (joinCompletedAt >= 0f && Time.realtimeSinceStartup - joinCompletedAt >= SecondsAfterJoin)
      return Slp2SaveChoice.None;
    var offer = new OfferState(forServerName, mods, joinCompletedAt);
    currentOffer = offer;
    SlpDialog.AddPanel(DrawCard);
    while (!offer.Completed)
    {
      if (offer.CompletedAt >= 0f && Time.realtimeSinceStartup - offer.CompletedAt >= SecondsAfterJoin)
        Complete(offer, Slp2SaveChoice.None);
      await UniTask.Yield();
    }
    return offer.Result;
  }

  public static void CancelOffer()
  {
    if (currentOffer is { } offer)
      Complete(offer, Slp2SaveChoice.None);
    joinCompletedAt = -1f;
  }

  private static bool Choose(Slp2SaveChoice choice)
  {
    if (currentOffer is { } offer)
      Complete(offer, choice);
    return true;
  }

  private static void Complete(OfferState offer, Slp2SaveChoice result)
  {
    if (offer.Completed)
      return;
    offer.Result = result;
    offer.Completed = true;
    if (ReferenceEquals(currentOffer, offer))
      currentOffer = null;
  }

  private static void DrawCard()
  {
    var offer = currentOffer;
    if (offer == null)
      return;
    // under the loading screen's progress bar
    var display = ImGui.GetIO().DisplaySize;
    SlpDialog.BeginPanel("##Slp2Offer", new Vector2(display.x * 0.5f, display.y * 0.5f + 60f), new Vector2(0.5f, 0f),
      SlpDialog.DefaultWidth);

    var count = offer.Mods.Count;
    SlpDialog.Title($"{offer.Name} offers a server modpack with {count} mod{(count == 1 ? "" : "s")}:");
    ImGui.Spacing();

    var versionX = ImGui.GetContentRegionAvail().x * 0.72f;
    ImGui.Indent();
    foreach (var mod in offer.Mods.Take(MaxListedMods))
    {
      var x = ImGui.GetCursorPosX();
      ImGuiHelper.TextColored(mod.Name ?? "", LaunchPadTheme.TextSub);
      var version = mod.Version?.Trim().TrimStart('v', 'V');
      if (!string.IsNullOrEmpty(version))
      {
        ImGui.SameLine(x + versionX);
        ImGuiHelper.TextColored($"v{version}", LaunchPadTheme.TextMuted);
      }
    }
    if (count > MaxListedMods)
      ImGuiHelper.TextColored($"and {count - MaxListedMods} more", LaunchPadTheme.TextMuted);
    ImGui.Unindent();
    ImGui.Spacing();

    SlpDialog.Text("Save it below, then pick it the next time you start the game. SLP loads exactly these mods, "
      + "validates them against the server and can join you straight in after loading!");
    SlpDialog.Gap();
    var clicked = SlpDialog.ButtonRow(buttons);
    if (clicked >= 0)
      buttons[clicked].OnClick();
    ImGui.End();
  }
}
