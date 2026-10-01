using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Assets.Scripts;
using Assets.Scripts.Networking;
using HarmonyLib;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;
using UI;
using UnityEngine.Events;

namespace StationeersLaunchPad.Networking;

// when a join fails because the mods differ, the game's error box explains what the server
// runs and offers to save it as a pack
[HarmonyPatch]
internal static class Slp2JoinFailureOffer
{
  private const int MaxListedMods = 8;

  private static bool reachedGame;
  private static bool offered;

  internal static void Initialize() => NetworkClient.ClientFinishedJoining += () =>
  {
    reachedGame = true;
    UI.Slp2SaveProfilePanel.OnJoinFinished();
  };

  // first message of every connection attempt
  [HarmonyPatch(typeof(NetworkMessages.VerifyPlayerRequest), "Deserialize"), HarmonyPostfix]
  private static void PostfixNewAttempt()
  {
    reachedGame = false;
    offered = false;
  }

  [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.EndConnection)), HarmonyPostfix]
  private static void PostfixEndConnection()
  {
    Slp2SaveFlow.CancelPending();
    Slp2JoinPiggyback.TakeReceivedCode();
  }

  [HarmonyPatch(typeof(ConfirmationPanel), nameof(ConfirmationPanel.ShowRaw)), HarmonyPrefix]
  private static void PrefixShowRaw(
    ref string title, ref string message,
    ref string button1Text, ref UnityAction button1OnClick,
    ref string button2Text, ref UnityAction button2OnClick)
  {
    if (Platform.IsServer || offered || reachedGame || Slp2Channel.ProbePending
      || !Configs.ServerPacksEnabled.Value || NetworkClient.ConnectionMethod != ConnectionMethod.RocketNet)
      return;
    var code = Slp2JoinPiggyback.LastReceivedCode;
    var address = NetworkClient.Address;
    if (code == null || string.IsNullOrEmpty(address) || !ushort.TryParse(NetworkClient.Port, out var port)
      || !Slp2PackageCode.TryDecode(code, out var entries, out var isServerCode, out var serverName)
      || !isServerCode || !Slp2ServerMatch.IsMatchableName(serverName))
      return;

    var modList = LaunchPadConfig.ModList;
    var serverMods = entries.Select(entry => (entry, mod: FindInstalled(entry, modList))).ToList();
    var extra = modList.EnabledMods
      .Where(mod => mod.Source != ModSourceType.Core && !serverMods.Any(server => server.mod == mod))
      .ToList();
    if (serverMods.All(server => server.mod is { Enabled: true }) && extra.Count == 0)
      return; // the mods match, it failed for another reason
    offered = true;

    var reason = CleanReason(message);
    var close = button1OnClick;
    title = $"Your mods don't match {serverName}";
    message = BuildMessage(serverName, serverMods, extra, reason);
    button1Text = "Save as pack";
    button1OnClick = () =>
    {
      var profileManager = LaunchPadConfig.ProfileManager;
      if (profileManager.SaveServerProfileFromCode(serverName, code, entries, address, port, activate: true))
        Logger.Global.Log($"Saved server pack '{profileManager.ActiveProfileName}', it loads on the next start");
      else
        Logger.Global.LogWarning($"Failed to save server pack for '{serverName}'");
      close?.Invoke();
    };
    button2Text = Localization.GetInterface("ButtonOk");
    button2OnClick = close;
  }

  private static ModInfo FindInstalled(Slp2PackageCode.Entry entry, ModList modList) =>
    modList.AllMods.FirstOrDefault(mod => mod.Source == ModSourceType.Workshop && mod.WorkshopHandle == entry.WorkshopHandle);

  private static string BuildMessage(string serverName,
    List<(Slp2PackageCode.Entry entry, ModInfo mod)> serverMods, List<ModInfo> extra, string reason)
  {
    var sb = new StringBuilder();
    sb.AppendLine($"{serverName} runs {serverMods.Count} mod{(serverMods.Count == 1 ? "" : "s")}:");
    foreach (var (entry, mod) in serverMods.Take(MaxListedMods))
    {
      var state = mod == null ? "not installed" : mod.Enabled ? "loaded" : "installed";
      sb.AppendLine($"  {entry.Name}  ({state})");
    }
    if (serverMods.Count > MaxListedMods)
      sb.AppendLine($"  and {serverMods.Count - MaxListedMods} more");
    if (extra.Count > 0)
      sb.AppendLine($"You also have {extra.Count} mod{(extra.Count == 1 ? "" : "s")} loaded the server doesn't run.");
    sb.AppendLine();
    sb.Append(Configs.ServerPacksAutoJoin.Value
      ? "Save them as a pack and restart the game. SLP loads them and joins the server for you."
      : "Save them as a pack and restart the game to load them.");
    if (!string.IsNullOrEmpty(reason))
      sb.Append($"\n\nServer: {reason}");
    return sb.ToString();
  }

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
