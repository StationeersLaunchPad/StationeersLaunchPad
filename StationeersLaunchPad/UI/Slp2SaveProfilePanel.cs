
using System;
using System.Linq;
using Assets.Scripts;
using Assets.Scripts.UI;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using ImGuiNET;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public static class Slp2SaveProfilePanel
{
  private sealed class OfferState(string name, bool mismatch, float completedAt)
  {
    internal readonly string Name = name;
    internal readonly bool Mismatch = mismatch;
    internal float CompletedAt = completedAt;
    internal bool Completed;
    internal bool Result;
  }

  private static Harmony harmony;
  private static OfferState currentOffer;
  private static float joinCompletedAt = -1f;

  // the offer stays up while loading and for ten seconds after, failed joins keep it until dismissed
  public static void OnJoinFinished()
  {
    joinCompletedAt = Time.realtimeSinceStartup;
    if (currentOffer is { Mismatch: false } offer)
      offer.CompletedAt = joinCompletedAt;
  }

  private static bool EnsurePatch()
  {
    harmony ??= new("SLP2SaveProfile");
    try
    {
      var patchMethod = typeof(Slp2SaveProfilePanel).GetMethod(nameof(Draw));
      if (patchMethod == null)
        return false;
      foreach (var target in new[]
      {
        typeof(OrbitalSimulation).GetMethod(nameof(OrbitalSimulation.Draw)),
        typeof(ImGuiLoadingScreen).GetMethod(nameof(ImGuiLoadingScreen.DrawStandardLoading)),
      })
      {
        if (target == null)
          return false;
        var info = Harmony.GetPatchInfo(target);
        if (info?.Postfixes.Any(patch => patch.PatchMethod == patchMethod) != true)
          harmony.Patch(target, postfix: new(patchMethod));
      }
      return true;
    }
    catch (Exception ex)
    {
      Logger.Global.LogWarning("SLP2 save-pack popup could not be installed");
      Logger.Global.LogException(ex);
      return false;
    }
  }

  // returns true if the user chose to save
  public static async UniTask<bool> Offer(string forServerName) =>
    await OfferInternal(forServerName, mismatch: false);

  public static async UniTask<bool> OfferMismatch(string forServerName) =>
    await OfferInternal(forServerName, mismatch: true);

  public static void CancelSuccessOffer()
  {
    if (currentOffer is { Mismatch: false } offer)
      Complete(offer, false);
  }

  public static void CancelOffer()
  {
    if (currentOffer is { } offer)
      Complete(offer, false);
    joinCompletedAt = -1f;
  }

  private static void Complete(OfferState offer, bool result)
  {
    if (offer.Completed)
      return;
    offer.Result = result;
    offer.Completed = true;
    if (ReferenceEquals(currentOffer, offer))
      currentOffer = null;
  }

  private static async UniTask<bool> OfferInternal(string forServerName, bool mismatch)
  {
    if (Platform.IsServer || currentOffer != null || !EnsurePatch())
      return false;
    if (!mismatch && joinCompletedAt >= 0f
      && Time.realtimeSinceStartup - joinCompletedAt >= 10f)
      return false;
    var offer = new OfferState(forServerName, mismatch, mismatch ? -1f : joinCompletedAt);
    currentOffer = offer;
    while (!offer.Completed)
    {
      if (!offer.Mismatch && offer.CompletedAt >= 0f
        && Time.realtimeSinceStartup - offer.CompletedAt >= 10f)
        Complete(offer, false);
      await UniTask.Yield();
    }
    return offer.Result;
  }

  public static void Draw()
  {
    if (currentOffer == null)
      return;
    ImGuiHelper.Draw(DrawCard);
  }

  private static void DrawCard()
  {
    var offer = currentOffer;
    if (offer == null)
      return;
    var display = ImGui.GetIO().DisplaySize;
    var width = Math.Min(720f, display.x - 48f);
    var bottomInset = Math.Max(90f, display.y * 0.13f);
    ImGui.SetNextWindowPos(new(display.x * 0.5f, display.y - bottomInset), ImGuiCond.Always, new(0.5f, 1f));
    ImGui.SetNextWindowSize(new(width, 0f), ImGuiCond.Always);
    ImGui.Begin("SLP | Server pack##Slp2Offer",
      ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove
      | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
    ImGui.SetWindowFontScale(1.2f);
    ImGuiHelper.Text($"Save {offer.Name}'s mod list?");
    ImGui.TextWrapped(offer.Mismatch
      ? "The join ended before you got in. SLP can load these mods as a pack on your next attempt."
      : "SLP will load this pack the next time you start the game. Your current join continues.");
    ImGui.Separator();
    var buttonWidth = ImGui.GetContentRegionAvail().x / 2f - 4f;
    if (ImGui.Button("Save as pack",
      new(buttonWidth, ImGui.GetTextLineHeightWithSpacing() * 1.35f)))
      Complete(offer, true);
    ImGui.SameLine();
    if (ImGui.Button("Not now", new(buttonWidth, ImGui.GetTextLineHeightWithSpacing() * 1.35f)))
      Complete(offer, false);
    ImGui.End();
  }
}
