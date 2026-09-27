using System;
using System.Text;
using Assets.Scripts.Networking;
using HarmonyLib;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.UI;

namespace StationeersLaunchPad.Networking;

// appends the server's SLP2 code to VerifyPlayerRequest, which is sent on every connection
// before the join is validated, so clients get the mod list even when the join is rejected.
// codes that don't fit send a marker instead and are fetched over Slp2Channel after joining.
// runs before Booster's postfixes so our string always sits right after the base fields
[HarmonyPatch]
internal static class Slp2JoinPiggyback
{
  private const string FetchMarker = "SLP2:FETCH";
  private const int TrailingReserveBytes = 16;

  // null until a valid code was read during this connection attempt
  internal static string LastReceivedCode { get; private set; }
  internal static bool NeedsChannelFetch { get; private set; }
  internal static int AttemptId { get; private set; }

  internal static string LastReceivedServerName { get; private set; }

  // clears the code so a later connection can't reuse it
  internal static string TakeReceivedCode()
  {
    var code = LastReceivedCode;
    LastReceivedCode = null;
    LastReceivedServerName = null;
    NeedsChannelFetch = false;
    return code;
  }

  // a connection that times out never gets VerifyPlayerRequest, so reset here too
  [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.StartClient),
    [typeof(string), typeof(ushort), typeof(ushort)]), HarmonyPostfix]
  private static void PostfixStartClient()
  {
    AttemptId++;
    TakeReceivedCode();
    Slp2SaveProfilePanel.CancelOffer();
  }

  [HarmonyPatch(typeof(NetworkMessages.VerifyPlayerRequest), "Serialize"), HarmonyPostfix]
  [HarmonyPriority(Priority.First)]
  private static void PostfixSerialize(RocketBinaryWriter writer)
  {
    if (!NetworkManager.IsServer)
      return;

    string code;
    try
    {
      code = Slp2Channel.ServerCodeProvider?.Invoke() ?? "";
    }
    catch (Exception ex)
    {
      Logger.Global.LogWarning("SLP2: failed to build server code for join piggyback");
      Logger.Global.LogException(ex);
      code = "";
    }
    // oversized packets get truncated by the receiver, leave room for Booster's header
    var room = Slp2Channel.MaxNetworkDataBytes - TrailingReserveBytes - writer.Position - 4;
    if (room < 0)
      return;
    if (Encoding.UTF8.GetByteCount(code) > room)
    {
      Logger.Global.LogWarning("SLP2 code exceeds handshake packet; requesting it on the channel after join");
      code = FetchMarker.Length <= room ? FetchMarker : "";
    }
    try
    {
      writer.WriteString(code);
    }
    catch (OverflowException)
    {
      Logger.Global.LogWarning("SLP2 code did not fit handshake writer; omitting join piggyback");
    }
  }

  [HarmonyPatch(typeof(NetworkMessages.VerifyPlayerRequest), "Deserialize"), HarmonyPostfix]
  [HarmonyPriority(Priority.First)]
  private static void PostfixDeserialize(RocketBinaryReader reader)
  {
    LastReceivedCode = null;
    LastReceivedServerName = null;
    NeedsChannelFetch = false;
    try
    {
      // always consume the string so Booster reads its header at the right offset
      var code = reader.ReadString();
      if (!Configs.ServerProfilesEnabled.Value)
        return;
      if (code == FetchMarker)
      {
        NeedsChannelFetch = true;
        return;
      }
      if (!string.IsNullOrEmpty(code) && Slp2PackageCode.TryDecode(code, out _, out _, out var serverName))
      {
        LastReceivedCode = code;
        LastReceivedServerName = serverName;
      }
    }
    catch (Exception)
    {
      // server without SLP2
    }
  }
}
