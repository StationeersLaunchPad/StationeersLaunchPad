
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Assets.Scripts;
using Assets.Scripts.Networking;
using Assets.Scripts.Util;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using UI;
using UnityEngine;
using UnityEngine.Events;

namespace StationeersLaunchPad.Networking;

// when a join fails on a server that shares its mods, the game's error box is held back while
// SLP asks the server for them, then shows what differs and offers to save them as a pack
[HarmonyPatch]
internal static class Slp2JoinFailureOffer
{
  private const int MaxListedMods = 8;
  private const float OfflineWaitSeconds = 3f;

  private static bool reachedGame;
  private static bool offered;
  private static bool passThrough;

  internal static void Initialize() => NetworkClient.ClientFinishedJoining += () =>
  {
    reachedGame = true;
    UI.Slp2SaveProfilePanel.OnJoinFinished();
  };

  // first message of every connection attempt
  [HarmonyPatch(typeof(NetworkMessages.VerifyPlayerRequest), "Deserialize"), HarmonyPostfix]
  private static void PostfixNewAttempt()
  {
    if (Slp2Channel.ProbePending)
      return;
    reachedGame = false;
    offered = false;
  }

  // the join was accepted, boxes from here on aren't join failures
  [HarmonyPatch(typeof(NetworkClient), "ProcessJoinData"), HarmonyPrefix]
  private static void PrefixJoinData() => reachedGame = true;

  [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.EndConnection)), HarmonyPostfix]
  private static void PostfixEndConnection() => UI.Slp2SaveProfilePanel.CancelOffer();

  [HarmonyPatch(typeof(ConfirmationPanel), nameof(ConfirmationPanel.ShowRaw)), HarmonyPrefix]
  private static bool PrefixShowRaw(
    string title, string message,
    string button1Text, UnityAction button1OnClick,
    string button2Text, UnityAction button2OnClick)
  {
    if (passThrough || Platform.IsServer || offered || reachedGame || Slp2Channel.ProbePending
      || !Configs.ServerPacksEnabled.Value || !Slp2JoinMarker.ServerSharesMods)
      return true;
    var serverName = Slp2JoinMarker.ServerName;
    var address = Slp2JoinMarker.Address;
    var port = Slp2JoinMarker.Port;
    if (!Slp2ServerMatch.IsMatchableName(serverName) || string.IsNullOrEmpty(address) || port == 0)
      return true;
    offered = true;

    var original = new Box(title, message, button1Text, button1OnClick, button2Text, button2OnClick);
    OfferAsync(original, serverName, address, port).Forget();
    return false;
  }

  private sealed class Box(string title, string message,
    string button1Text, UnityAction button1OnClick, string button2Text, UnityAction button2OnClick)
  {
    internal readonly string Title = title;
    internal readonly string Message = message;
    internal readonly string Button1Text = button1Text;
    internal readonly UnityAction Button1OnClick = button1OnClick;
    internal readonly string Button2Text = button2Text;
    internal readonly UnityAction Button2OnClick = button2OnClick;
  }

  private static async UniTaskVoid OfferAsync(Box original, string serverName, string address, ushort port)
  {
    var panel = Singleton<ConfirmationPanel>.Instance;
    try
    {
      await Offer(panel, original, serverName, address, port);
    }
    catch (Exception ex)
    {
      // the game's own box must still show
      Logger.Global.LogException(ex);
      Show(panel, original.Title, original.Message,
        original.Button1Text, original.Button1OnClick, original.Button2Text, original.Button2OnClick);
    }
  }

  private static async UniTask Offer(ConfirmationPanel panel, Box original, string serverName, string address, ushort port)
  {
    var cancellation = new CancellationTokenSource();
    Show(panel, original.Title, $"Checking {serverName}'s mods...", "Skip", () =>
    {
      cancellation.Cancel();
      panel.CloseCurrentPanel();
    });

    Slp2Query.Result result = null;
    try
    {
      // the probe needs the failed connection to be closed first
      var deadline = Time.realtimeSinceStartup + OfflineWaitSeconds;
      while (NetworkManager.NetworkState != NetworkState.Offline && Time.realtimeSinceStartup < deadline
        && !cancellation.IsCancellationRequested)
        await UniTask.Yield();
      if (!cancellation.IsCancellationRequested)
        result = await Slp2Query.Fetch(address, port, cancellation.Token);
    }
    catch (Exception ex)
    {
      Logger.Global.LogWarning($"SLP2: asking {serverName} for its mods failed");
      Logger.Global.LogException(ex);
    }
    if (!cancellation.IsCancellationRequested)
      panel.CloseCurrentPanel();

    var rows = result == null ? null
      : Slp2ModCompare.Compare(result.Entries, LaunchPadConfig.ModList, LaunchPadConfig.ProfileManager);
    // no answer, or the mods match and it failed for another reason
    if (rows == null || Slp2ModCompare.AllMatch(rows))
    {
      Show(panel, original.Title, original.Message,
        original.Button1Text, original.Button1OnClick, original.Button2Text, original.Button2OnClick);
      return;
    }

    var close = original.Button1OnClick ?? panel.CloseCurrentPanel;
    Show(panel, $"Your mods don't match {result.ServerName}", BuildMessage(result.ServerName, rows, CleanReason(original.Message)),
      "Save server pack", () =>
      {
        close();
        Save(result, address, port);
      },
      Localization.GetInterface("ButtonOk"), close);
  }

  private static void Save(Slp2Query.Result result, string address, ushort port)
  {
    var profileManager = LaunchPadConfig.ProfileManager;
    if (profileManager.SaveServerProfileFromCode(result.ServerName, result.Code, result.Entries, address, port, activate: true))
      Logger.Global.Log($"Saved server pack '{profileManager.ActiveProfileName}', it loads on the next start");
    else
      Logger.Global.LogWarning($"Failed to save server pack for '{result.ServerName}'");
  }

  private static void Show(ConfirmationPanel panel, string title, string message,
    string button1Text, UnityAction button1OnClick, string button2Text = null, UnityAction button2OnClick = null)
  {
    passThrough = true;
    try
    {
      panel.ShowRaw(title, message, button1Text, button1OnClick, button2Text, button2OnClick);
    }
    finally
    {
      passThrough = false;
    }
  }

  private static string BuildMessage(string serverName, List<Slp2ModRow> rows, string reason)
  {
    var serverMods = rows.Where(row => row.State != Slp2ModState.NotOnServer).ToList();
    var extra = rows.Count - serverMods.Count;
    var sb = new StringBuilder();
    sb.AppendLine($"{serverName} runs {serverMods.Count} mod{(serverMods.Count == 1 ? "" : "s")}:");
    // the ones that differ first
    foreach (var row in serverMods.OrderBy(row => row.State == Slp2ModState.Match).Take(MaxListedMods))
      sb.AppendLine($"  {row.Name}{Versioned(row.ServerVersion)}  ({Describe(row)})");
    if (serverMods.Count > MaxListedMods)
      sb.AppendLine($"  and {serverMods.Count - MaxListedMods} more");
    if (extra > 0)
      sb.AppendLine($"You also have {extra} mod{(extra == 1 ? "" : "s")} loaded the server doesn't run.");
    sb.AppendLine();
    sb.Append("Save them as a server pack and restart the game. SLP switches to it, loads exactly these mods");
    sb.Append(Configs.ServerPacksAutoConnect.Value ? " and joins the server for you." : " and checks them against the server.");
    sb.Append(" Next time, just pick it when the game starts.");
    if (!string.IsNullOrEmpty(reason))
      sb.Append($"\n\nServer: {reason}");
    return sb.ToString();
  }

  private static string Versioned(string version) => string.IsNullOrEmpty(version) ? "" : $" v{version.TrimStart('v', 'V')}";

  private static string Describe(Slp2ModRow row) => row.State switch
  {
    Slp2ModState.NotInstalled => "not installed",
    Slp2ModState.NotEnabled => "installed, not loaded",
    Slp2ModState.Newer => $"you have{Versioned(row.LocalVersion)}",
    Slp2ModState.Older => $"you have{Versioned(row.LocalVersion)}",
    _ => "loaded",
  };

  // rejection texts that aren't localization keys come through as <T:EN:text>
  private static string CleanReason(string message)
  {
    if (string.IsNullOrWhiteSpace(message))
      return "";
    var text = message.Trim();
    if (text.StartsWith("<T:") && text.EndsWith(">"))
    {
      var start = text.IndexOf(':', 3);
      text = start < 0 ? text : text.Substring(start + 1, text.Length - start - 2);
    }
    return text.Trim();
  }
}
