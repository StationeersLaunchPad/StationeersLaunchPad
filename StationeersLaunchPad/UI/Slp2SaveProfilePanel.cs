
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
    // sits under the loading screen's progress bar, in its style
    var display = ImGui.GetIO().DisplaySize;
    var width = Math.Min(560f, display.x - 48f);
    ImGui.SetNextWindowPos(new(display.x * 0.5f, display.y * 0.5f + 60f), ImGuiCond.Always, new(0.5f, 0f));
    ImGui.SetNextWindowSize(new(width, 0f), ImGuiCond.Always);
    ImGui.Begin("##Slp2Offer", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground
      | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize);
    CenteredText($"{offer.Name} runs these mods.", LaunchPadTheme.Text);
    CenteredText("Save them as a pack to join with one click next time.", LaunchPadTheme.TextSub);
    ImGui.Spacing();
    var style = ImGui.GetStyle();
    var buttonWidth = 170f;
    var buttonHeight = ImGui.GetFrameHeight() * 1.3f;
    ImGui.SetCursorPosX((ImGui.GetWindowWidth() - buttonWidth * 2f - style.ItemSpacing.x) / 2f);
    if (Widgets.PrimaryButton("Save as pack##slp2save", new(buttonWidth, buttonHeight), true, 1f))
      Complete(offer, true);
    ImGui.SameLine();
    if (ImGui.Button("No thanks##slp2skip", new(buttonWidth, buttonHeight)))
      Complete(offer, false);
    ImGui.End();
  }

  private static void CenteredText(string text, Color color)
  {
    ImGui.SetCursorPosX((ImGui.GetWindowWidth() - ImGui.CalcTextSize(text).x) / 2f);
    ImGuiHelper.TextColored(text, color);
  }
}
