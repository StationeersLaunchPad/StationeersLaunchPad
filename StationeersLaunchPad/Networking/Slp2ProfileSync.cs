
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ImGuiNET;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.UI;
using UnityEngine;

namespace StationeersLaunchPad.Networking;

// checks a server pack against its server while mods load, and waits for the result
// right before StartGame(). only a confirmed mismatch stops with a warning, an unreachable
// server is left to the real join
internal static class Slp2ProfileSync
{
  private const float GraceSeconds = 4f;
  private const float CancelGraceSeconds = 2f;

  private enum VerifyState { None, Pending, Verified, Failed }
  private static VerifyState state;
  private static string failReason;
  private static bool warningVisible;
  private static bool isConfirmedMismatch;
  private static string mismatchCode;
  private static ProfileData mismatchProfile;
  private static string mismatchAddress;
  private static ushort mismatchPort;
  private static CancellationTokenSource verifyCancellation;
  private static bool probeRunning;

  internal static bool WasVerified => state == VerifyState.Verified;
  internal static string VerifiedAddress { get; private set; }
  internal static ushort VerifiedPort { get; private set; }

  internal static (string text, ProfileStatusKind kind)? SyncStatus => state switch
  {
    VerifyState.Pending => ("Checking server for auto-connect...", ProfileStatusKind.Info),
    VerifyState.Verified => (
      Configs.ServerPacksAutoJoin.Value
        ? "Server pack checked: the server matches, joining after load"
        : "Server pack checked: the server matches",
      ProfileStatusKind.Saved),
    VerifyState.Failed when isConfirmedMismatch => ("Server mods changed - see warning before loading finishes", ProfileStatusKind.Unsaved),
    VerifyState.Failed => ("Could not verify - won't auto-connect", ProfileStatusKind.Error),
    _ => null,
  };

  // the status is stale once the main menu is up
  internal static void Initialize() => WorldManager.OnGameDataLoaded += () => state = VerifyState.None;

  internal static void TryStartVerify(ProfileManager profileManager)
  {
    verifyCancellation?.Cancel();
    verifyCancellation?.Dispose();
    verifyCancellation = null;
    state = VerifyState.None;
    VerifiedAddress = null;
    VerifiedPort = 0;
    mismatchCode = null;
    mismatchProfile = null;
    var profile = profileManager.ActiveProfile;
    if (Platform.IsServer || !Configs.ServerPacksEnabled.Value
      || profile == null || string.IsNullOrEmpty(profile.ServerName)
      || !Slp2PackageCode.TryDecode(profile.ServerCode, out _, out var isServerCode, out var savedName)
      || !isServerCode || string.IsNullOrEmpty(savedName))
      return;

    state = VerifyState.Pending;
    verifyCancellation = new CancellationTokenSource();
    VerifyAsync(profile, verifyCancellation.Token).Forget();
  }

  private static async UniTaskVoid VerifyAsync(ProfileData profile, CancellationToken cancellationToken)
  {
    try
    {
      string address;
      ushort port;
      // the address can change, prefer the server list and fall back to the saved one
      var session = await Slp2ServerMatch.FindByName(profile.ServerName);
      // the lookup ignores cancellation, the gate already decided if it gave up
      if (cancellationToken.IsCancellationRequested)
        return;
      if (session != null && ushort.TryParse(session.Port, out port))
      {
        address = session.Address;
      }
      else if (!string.IsNullOrEmpty(profile.LastAddress) && profile.LastPort != 0)
      {
        address = profile.LastAddress;
        port = profile.LastPort;
      }
      else
      {
        Fail($"server '{profile.ServerName}' not found in the current server list", confirmedMismatch: false);
        return;
      }

      string code;
      probeRunning = true;
      try
      {
        code = await Slp2ProbeClient.RequestServerCode(
          address, port, cancellationToken: cancellationToken);
      }
      finally
      {
        probeRunning = false;
      }
      if (string.IsNullOrEmpty(code)
        || !Slp2PackageCode.TryDecode(code, out _, out var isServerCode, out var embeddedName)
        || !isServerCode)
      {
        Fail($"could not verify '{profile.ServerName}'", confirmedMismatch: false);
        return;
      }

      // the address may now belong to a different server
      if (!profile.ServerName.Equals(embeddedName, StringComparison.OrdinalIgnoreCase))
      {
        Fail($"a different server answered for '{profile.ServerName}'", confirmedMismatch: false);
        return;
      }

      if (code != profile.ServerCode)
      {
        mismatchCode = code;
        mismatchProfile = profile;
        mismatchAddress = address;
        mismatchPort = port;
        Fail($"'{profile.ServerName}' no longer matches its server pack", confirmedMismatch: true);
        return;
      }

      VerifiedAddress = address;
      VerifiedPort = port;
      state = VerifyState.Verified;
    }
    catch (Exception ex)
    {
      Logger.Global.LogWarning($"SLP2 verification failed for '{profile.ServerName}'");
      Logger.Global.LogException(ex);
      Fail($"could not verify '{profile.ServerName}'", confirmedMismatch: false);
    }
  }

  private static void Fail(string reason, bool confirmedMismatch)
  {
    failReason = reason;
    isConfirmedMismatch = confirmedMismatch;
    state = VerifyState.Failed;
  }

  // called right before StartGame(), waits a bit for a pending verify
  internal static async UniTask WaitForGate()
  {
    if (state == VerifyState.None)
      return;

    var deadline = Time.realtimeSinceStartup + GraceSeconds;
    while (state == VerifyState.Pending && Time.realtimeSinceStartup < deadline)
      await UniTask.Yield();

    if (state == VerifyState.Pending)
    {
      verifyCancellation?.Cancel();
      // a cancelled probe needs a few frames to release its bootstrap manager
      var cancelDeadline = Time.realtimeSinceStartup + CancelGraceSeconds;
      while (state == VerifyState.Pending
        && (probeRunning || Time.realtimeSinceStartup < cancelDeadline))
        await UniTask.Yield();
      if (state == VerifyState.Pending)
        Fail("server list lookup timed out", confirmedMismatch: false);
    }

    if (state != VerifyState.Failed || !isConfirmedMismatch)
      return; // verified, still pending (timed out inconclusively), or inconclusive - let it ride

    warningVisible = true;
    while (warningVisible)
      await UniTask.Yield();
  }

  internal static void DrawStatusIfActive()
  {
    var status = SyncStatus;
    if (status == null)
      return;

    ImGuiHelper.Draw(() =>
    {
      var screen = ImGuiHelper.ScreenRect().Shrink(25f);
      ImGui.SetNextWindowPos(
        new Vector2(screen.Max.x, screen.Min.y), ImGuiCond.Always, new Vector2(1f, 0f));
      ImGui.Begin("##slp2syncstatus",
        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings
        | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing
        | ImGuiWindowFlags.NoInputs);
      ImGuiHelper.TextColored(status.Value.text, ProfileStatusIndicator.ColorFor(status.Value.kind));
      ImGui.End();
    });
  }

  internal static void DrawWarningIfVisible()
  {
    if (!warningVisible)
      return;

    ImGuiHelper.Draw(() =>
    {
      const string modalName = "Server Mods Changed##Slp2Verify";
      ImGui.OpenPopup(modalName);
      ImGui.BeginPopupModal(modalName, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize);

      ImGuiHelper.Text(failReason ?? "unknown reason");
      ImGuiHelper.Text("The server's mods changed. Update the pack for next time,");
      ImGuiHelper.Text("or continue anyway with what's currently loaded.");
      ImGui.Separator();

      var width = ImGui.GetContentRegionAvail().x / 2 - 4;
      if (ImGui.Button("Update Pack", new(width, ImGui.GetTextLineHeightWithSpacing())))
      {
        ApplyMismatchUpdate();
        warningVisible = false;
      }
      ImGui.SameLine();
      if (ImGui.Button("Continue Anyway", new(width, ImGui.GetTextLineHeightWithSpacing())))
        warningVisible = false;
      ImGui.EndPopup();
    });
  }

  private static void ApplyMismatchUpdate()
  {
    if (mismatchProfile == null || string.IsNullOrEmpty(mismatchCode))
      return;
    if (!Slp2PackageCode.TryDecode(mismatchCode, out var entries, out _, out _))
      return;

    if (!LaunchPadConfig.ProfileManager.SaveServerProfileFromCode(
      mismatchProfile.ServerName, mismatchCode, entries, mismatchAddress, mismatchPort, activate: true))
      Logger.Global.LogWarning($"Failed to update server pack for '{mismatchProfile.ServerName}'");
  }
}
