
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ImGuiNET;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.UI;
using UnityEngine;

namespace StationeersLaunchPad.Networking;

// asks a server pack's server for its mods during the countdown, and compares them with what's
// about to load right before loading. a match joins after load, a mismatch shows what differs
internal static class Slp2ProfileSync
{
  private const float GraceSeconds = 4f;
  private const float CancelGraceSeconds = 2f;

  private enum VerifyState { None, Pending, Fetched, Unreachable }
  private static VerifyState state;
  private static string failReason;
  private static ProfileData profile;
  private static Slp2Query.Result result;
  private static string address;
  private static ushort port;
  private static CancellationTokenSource verifyCancellation;
  private static bool probeRunning;

  private static bool decided;
  private static bool join;
  private static bool mismatchVisible;

  internal static bool WasVerified => decided && join;
  internal static string VerifiedAddress => address;
  internal static ushort VerifiedPort => port;

  internal static (string text, ProfileStatusKind kind)? SyncStatus
  {
    get
    {
      var name = profile?.ServerName;
      if (decided)
        return join ? ($"{name}: validated, joining after load", ProfileStatusKind.Saved)
          : state == VerifyState.Fetched && Matches() ? ($"{name}: validated", ProfileStatusKind.Saved)
          : ($"{name}: not joining after load", ProfileStatusKind.Unsaved);
      return state switch
      {
        VerifyState.Pending => ($"Checking {name}...", ProfileStatusKind.Info),
        VerifyState.Fetched when Matches() => ($"{name}: your mods match", ProfileStatusKind.Saved),
        VerifyState.Fetched => ($"{name} runs different mods", ProfileStatusKind.Unsaved),
        VerifyState.Unreachable => ($"Couldn't check {name}: {failReason}", ProfileStatusKind.Error),
        _ => null,
      };
    }
  }

  // the status is stale once the main menu is up
  internal static void Initialize() => WorldManager.OnGameDataLoaded += () =>
  {
    state = VerifyState.None;
    decided = false;
  };

  internal static void TryStartVerify(ProfileManager profileManager)
  {
    verifyCancellation?.Cancel();
    verifyCancellation?.Dispose();
    verifyCancellation = null;
    state = VerifyState.None;
    decided = false;
    profile = null;
    result = null;
    address = null;
    port = 0;
    var active = profileManager.ActiveProfile;
    if (Platform.IsServer || !Configs.ServerPacksEnabled.Value || !ProfileManager.IsServerPack(active))
      return;

    profile = active;
    state = VerifyState.Pending;
    verifyCancellation = new CancellationTokenSource();
    VerifyAsync(active, verifyCancellation.Token).Forget();
  }

  private static async UniTaskVoid VerifyAsync(ProfileData target, CancellationToken cancellationToken)
  {
    try
    {
      // the address can change, prefer the server list and fall back to the saved one
      var session = await Slp2ServerMatch.FindByName(target.ServerName);
      if (cancellationToken.IsCancellationRequested)
        return;
      string targetAddress;
      ushort targetPort;
      if (session != null && ushort.TryParse(session.Port, out targetPort))
        targetAddress = session.Address;
      else if (!string.IsNullOrEmpty(target.LastAddress) && target.LastPort != 0)
      {
        targetAddress = target.LastAddress;
        targetPort = target.LastPort;
      }
      else
      {
        Fail("it isn't in the server list");
        return;
      }

      // a cancelled earlier check may still be closing its connection
      while (probeRunning && !cancellationToken.IsCancellationRequested)
        await UniTask.Yield();
      if (cancellationToken.IsCancellationRequested)
        return;
      Slp2Query.Result fetched;
      probeRunning = true;
      try
      {
        fetched = await Slp2Query.Fetch(targetAddress, targetPort, cancellationToken);
      }
      finally
      {
        probeRunning = false;
      }
      if (cancellationToken.IsCancellationRequested)
        return;
      if (fetched == null)
      {
        Fail("it didn't answer");
        return;
      }
      // the address may now belong to a different server
      if (!target.ServerName.Equals(fetched.ServerName, StringComparison.OrdinalIgnoreCase))
      {
        Fail($"'{fetched.ServerName}' answered instead");
        return;
      }

      result = fetched;
      address = targetAddress;
      port = targetPort;
      state = VerifyState.Fetched;
    }
    catch (Exception ex)
    {
      Logger.Global.LogWarning($"SLP2: checking '{target.ServerName}' failed");
      Logger.Global.LogException(ex);
      Fail("something went wrong, see the log");
    }
  }

  private static void Fail(string reason)
  {
    failReason = reason;
    state = VerifyState.Unreachable;
  }

  private static List<Slp2ModRow> Rows() =>
    result == null ? [] : Slp2ModCompare.Compare(result.Entries, LaunchPadConfig.ModList, LaunchPadConfig.ProfileManager);

  private static bool Matches() => Slp2ModCompare.AllMatch(Rows());

  // the server's list has mods the saved pack doesn't, or the other way around
  private static bool PackOutdated()
  {
    if (result == null || profile == null)
      return false;
    var saved = profile.Mods.Select(mod => mod.WorkshopHandle).ToHashSet();
    var server = result.Entries.Select(entry => entry.WorkshopHandle).ToHashSet();
    return !saved.SetEquals(server);
  }

  // called right before mods load, waits a bit for a pending check
  private static bool gating;

  // shown in the status bar instead of the countdown while the check holds loading back
  internal static string GateStatus => !gating || profile == null ? null
    : mismatchVisible ? $"{profile.ServerName} runs different mods - choose how to continue"
    : $"Checking {profile.ServerName}...";

  internal static async UniTask WaitForGate()
  {
    gating = true;
    try
    {
      await Gate();
    }
    finally
    {
      gating = false;
    }
  }

  private static async UniTask Gate()
  {
    var profileManager = LaunchPadConfig.ProfileManager;
    // the player picked another pack after the check started
    if (state != VerifyState.None && !ReferenceEquals(profileManager.ActiveProfile, profile))
      TryStartVerify(profileManager);
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
      while (probeRunning && Time.realtimeSinceStartup < cancelDeadline)
        await UniTask.Yield();
      Fail("it took too long to answer");
    }

    if (state != VerifyState.Fetched)
    {
      Decide(false);
      return;
    }

    // same mods but other versions on the server, keep the saved code current
    if (!PackOutdated() && result.Code != profile.ServerCode)
      profileManager.SaveServerProfileFromCode(profile.ServerName, result.Code, result.Entries, address, port, activate: true);

    if (Matches())
    {
      Decide(true);
      return;
    }

    mismatchVisible = true;
    while (mismatchVisible)
    {
      // downloaded mods reload the mod list, the check runs again after
      if (LaunchPadConfig.Reloading)
      {
        mismatchVisible = false;
        return;
      }
      await UniTask.Yield();
    }
  }

  // returns true so it can close a dialog button
  private static bool Decide(bool joinAfterLoad)
  {
    decided = true;
    join = joinAfterLoad && Configs.ServerPacksAutoConnect.Value;
    mismatchVisible = false;
    return true;
  }

  private static void UpdatePack()
  {
    if (!LaunchPadConfig.ProfileManager.SaveServerProfileFromCode(
      profile.ServerName, result.Code, result.Entries, address, port, activate: true))
    {
      Logger.Global.LogWarning($"Failed to update server pack for '{profile.ServerName}'");
      return;
    }
    LaunchPadConfig.ReapplyActivePack();
    if (Matches())
      Decide(true);
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
    if (!mismatchVisible || result == null)
      return;

    ImGuiHelper.Draw(() =>
    {
      // over the plain splash, the rest of SLP steps aside while it's open
      SlpDialog.BeginPanel("##Slp2ServerMods", ImGui.GetIO().DisplaySize * 0.5f, new Vector2(0.5f, 0.5f), 640f, focus: true);

      var rows = Rows();
      var serverRows = rows.Count(row => row.State != Slp2ModState.NotOnServer);
      var matching = rows.Count(row => row.State == Slp2ModState.Match);
      SlpDialog.Title($"{profile.ServerName} runs different mods than you");
      SlpDialog.Text($"{matching} of the server's {serverRows} mods match yours.");
      ImGui.Spacing();

      DrawTable(rows.Where(row => row.State != Slp2ModState.Match).ToList());
      ImGui.Spacing();
      DrawHints(rows);
      SlpDialog.Gap();

      var buttons = Buttons(rows);
      var clicked = SlpDialog.ButtonRow(buttons);
      if (clicked >= 0)
        buttons[clicked].OnClick();
      ImGui.End();
    });
  }

  private static List<DialogButton> Buttons(List<Slp2ModRow> rows)
  {
    var buttons = new List<DialogButton>();
    if (PackOutdated())
      buttons.Add(new("Update pack", () =>
      {
        UpdatePack();
        return true;
      }, primary: true, tooltip: "Switches the pack to the server's mods, before anything loads."));

    var missing = rows.Where(row => row.State == Slp2ModState.NotInstalled).ToList();
    if (missing.Count > 0)
    {
      var downloading = ProfilePanel.Busy;
      buttons.Add(new(downloading ? "Downloading..." : $"Download {missing.Count}", () =>
      {
        ProfilePanel.Download([.. missing.Select(row => new ProfileModEntry
        {
          Name = row.Name,
          Source = Sources.ModSourceType.Workshop,
          WorkshopHandle = row.WorkshopHandle,
        })]);
        return true;
      }, primary: true,
        tooltip: Steam.Running
          ? $"Subscribes to the {missing.Count} missing mod{(missing.Count == 1 ? "" : "s")}, downloads them and checks the server again."
          : Steam.NotRunningText,
        enabled: () => Steam.Running && !ProfilePanel.Busy));
    }

    if (!Configs.ServerPacksAutoConnect.Value)
    {
      buttons.Add(new("Load anyway", () => Decide(false), tooltip: "Loads your mods as they are."));
      return buttons;
    }
    buttons.Add(new("Join anyway", () => Decide(true),
      tooltip: "Loads your mods as they are and joins the server after load."));
    buttons.Add(new("Don't join", () => Decide(false), cancel: true,
      tooltip: "Loads your mods as they are and stays in the main menu."));
    return buttons;
  }

  private static void DrawTable(List<Slp2ModRow> rows)
  {
    if (rows.Count == 0)
      return;
    // big packs scroll inside the table so the buttons below stay on screen
    var rowHeight = ImGui.GetTextLineHeight() + ImGui.GetStyle().CellPadding.y * 2f;
    var height = Math.Min(rowHeight * (rows.Count + 1) + 2f, ImGui.GetIO().DisplaySize.y * 0.4f);
    if (!ImGui.BeginTable("##slp2mods", 4,
      ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY,
      new Vector2(0f, height)))
      return;
    ImGui.TableSetupScrollFreeze(0, 1);
    ImGui.TableSetupColumn("Mod", ImGuiTableColumnFlags.WidthStretch, 3f);
    ImGui.TableSetupColumn("Server", ImGuiTableColumnFlags.WidthStretch, 1f);
    ImGui.TableSetupColumn("You", ImGuiTableColumnFlags.WidthStretch, 1f);
    ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthStretch, 2f);
    ImGui.TableHeadersRow();
    foreach (var row in rows)
    {
      var (status, color) = Describe(row.State);
      ImGui.TableNextRow();
      ImGui.TableNextColumn();
      ImGuiHelper.Text(row.Name ?? "");
      ImGui.TableNextColumn();
      DrawVersion(row.State == Slp2ModState.NotOnServer ? null : row.ServerVersion, LaunchPadTheme.Text);
      ImGui.TableNextColumn();
      DrawVersion(row.State == Slp2ModState.NotInstalled ? null : row.LocalVersion, color);
      ImGui.TableNextColumn();
      ImGuiHelper.TextColored(status, color);
    }
    ImGui.EndTable();
  }

  private static void DrawVersion(string version, Color color)
  {
    if (version == null)
      ImGuiHelper.TextDisabled("-");
    else if (version.Length == 0)
      ImGuiHelper.TextDisabled("no version");
    else
      ImGuiHelper.TextColored(version, color);
  }

  private static (string, Color) Describe(Slp2ModState state) => state switch
  {
    Slp2ModState.NotInstalled => ("not installed", LaunchPadTheme.Err),
    Slp2ModState.NotEnabled => ("not in your pack", LaunchPadTheme.Warn),
    Slp2ModState.Newer => ("yours is newer", LaunchPadTheme.Info),
    Slp2ModState.Older => ("yours is older", LaunchPadTheme.Warn),
    Slp2ModState.NotOnServer => ("the server doesn't run it", LaunchPadTheme.TextSub),
    _ => ("matches", LaunchPadTheme.Ok),
  };

  private static void DrawHints(List<Slp2ModRow> rows)
  {
    var hints = new List<string>();
    if (rows.Any(row => row.State == Slp2ModState.NotInstalled))
      hints.Add(Steam.Running
        ? "Missing mods: Download gets them from the Workshop, then SLP checks the server again."
        : $"Missing mods: {Steam.NotRunningText}");
    if (rows.Any(row => row.State == Slp2ModState.NotEnabled))
      hints.Add("Not in your pack: the server added mods since you saved this pack. Update pack adds them.");
    if (rows.Any(row => row.State == Slp2ModState.Older))
      hints.Add("Older versions: Steam usually updates them on the next start. If it doesn't, unsubscribe in the Workshop, "
        + "wait a bit and subscribe again to force the newest version.");
    if (rows.Any(row => row.State == Slp2ModState.Newer))
      hints.Add("Newer versions: the server hasn't updated yet. Ask its admin, or wait until they do.");
    if (rows.Any(row => row.State == Slp2ModState.NotOnServer))
      hints.Add("Mods the server doesn't run can stop you from joining. Mark them clientside if they don't need the server.");
    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(520f, ImGui.GetContentRegionAvail().x));
    foreach (var hint in hints)
      ImGuiHelper.TextColored(hint, LaunchPadTheme.TextSub);
    ImGui.PopTextWrapPos();
  }
}
