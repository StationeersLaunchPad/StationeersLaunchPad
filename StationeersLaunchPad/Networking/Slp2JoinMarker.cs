
using System;
using System.IO;
using System.Text;
using Assets.Scripts;
using Assets.Scripts.Networking;
using HarmonyLib;

namespace StationeersLaunchPad.Networking;

// servers that share their mods end VerifyPlayerRequest with a small footer saying so. it goes
// after everything else, Booster's header included, so older clients never read that far and
// newer ones find it from the end. the mods themselves are asked for with Slp2Query
[HarmonyPatch]
internal static class Slp2JoinMarker
{
  private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SLP2");
  private const byte Format = 1;
  // name length, format, magic
  private const int FooterBytes = 2 + 1 + 4;

  private static readonly AccessTools.FieldRef<RocketBinaryReader, Stream> readerStream =
    AccessTools.FieldRefAccess<RocketBinaryReader, Stream>("_stream");

  // the server's VerifyPlayerRequest arrived, marker or not
  internal static bool RequestReceived { get; private set; }
  internal static bool ServerSharesMods { get; private set; }
  internal static string ServerName { get; private set; }
  internal static string Address { get; private set; }
  internal static ushort Port { get; private set; }
  internal static int AttemptId { get; private set; }

  internal static void Reset()
  {
    RequestReceived = false;
    ServerSharesMods = false;
    ServerName = null;
    Address = null;
    Port = 0;
  }

  [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.StartClient),
    [typeof(string), typeof(ushort), typeof(ushort)]), HarmonyPostfix]
  private static void PostfixStartClient()
  {
    AttemptId++;
    Reset();
    UI.Slp2SaveProfilePanel.CancelOffer();
  }

  [HarmonyPatch(typeof(NetworkMessages.VerifyPlayerRequest), "Serialize"), HarmonyPostfix]
  [HarmonyPriority(Priority.Last)]
  private static void PostfixSerialize(RocketBinaryWriter writer)
  {
    if (!NetworkManager.IsServer)
      return;
    string serverName;
    try
    {
      serverName = Slp2Handshake.SharedServerName();
    }
    catch (Exception ex)
    {
      Logger.Global.LogWarning("SLP2: failed to check the shared mods for the join marker");
      Logger.Global.LogException(ex);
      return;
    }
    if (serverName == null)
      return;

    // a name that doesn't fit is left out, the client still gets it with the mods
    var name = Encoding.UTF8.GetBytes(serverName);
    var room = Slp2Channel.MaxNetworkDataBytes - writer.Position - FooterBytes;
    if (room < 0)
      return;
    if (name.Length > room || name.Length > ushort.MaxValue)
      name = [];
    try
    {
      writer.WriteBytes(name, name.Length);
      writer.WriteByte((byte)name.Length);
      writer.WriteByte((byte)(name.Length >> 8));
      writer.WriteByte(Format);
      writer.WriteBytes(Magic, Magic.Length);
    }
    catch (OverflowException)
    {
      Logger.Global.LogWarning("SLP2: join marker did not fit, players won't be offered this server's mods");
    }
  }

  [HarmonyPatch(typeof(NetworkMessages.VerifyPlayerRequest), "Deserialize"), HarmonyPostfix]
  private static void PostfixDeserialize(RocketBinaryReader reader)
  {
    Reset();
    RequestReceived = true;
    if (!Configs.ServerPacksEnabled.Value)
      return;
    var stream = readerStream(reader);
    if (stream is not { CanSeek: true } || stream.Length < FooterBytes)
      return;

    // read from the end and put the position back, so whatever reads next isn't affected
    var position = stream.Position;
    try
    {
      var footer = new byte[FooterBytes];
      stream.Position = stream.Length - FooterBytes;
      if (stream.Read(footer, 0, FooterBytes) != FooterBytes || footer[2] != Format
        || !footer.AsSpan(3).SequenceEqual(Magic))
        return;
      var nameLength = footer[0] | footer[1] << 8;
      if (nameLength > stream.Length - FooterBytes)
        return;
      var name = new byte[nameLength];
      stream.Position = stream.Length - FooterBytes - nameLength;
      if (stream.Read(name, 0, nameLength) != nameLength)
        return;

      ServerSharesMods = true;
      ServerName = Encoding.UTF8.GetString(name);
      Address = NetworkClient.Address;
      Port = ushort.TryParse(NetworkClient.Port, out var port) ? port : (ushort)0;
    }
    finally
    {
      stream.Position = position;
    }
  }
}
