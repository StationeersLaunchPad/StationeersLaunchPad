
using System;
using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Assets.Scripts.Networking;
using HarmonyLib;
using UnityEngine;

namespace StationeersLaunchPad.Networking;

internal static class Slp2Channel
{
  // 239 = 'S'+'L'+'P', Booster uses 169
  internal const NetworkChannel Channel = (NetworkChannel)239;

  private const byte KindRequest = 0;
  private const byte KindResponse = 1;

  // single packet only for now
  private const int MaxPayloadSize = 8192;

  private static byte[] networkManagerBuffer;
  private static byte[] NetworkManagerBuffer => networkManagerBuffer ??=
    (byte[])typeof(NetworkManager).GetField(
      "Buffer", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

  private static Func<long> getHostId;
  internal static long GetHostId() => (getHostId ??= BuildHostIdGetter())();

  private static Func<long> BuildHostIdGetter()
  {
    var field = typeof(NetworkManager).GetField("_hostId", BindingFlags.Static | BindingFlags.NonPublic);
    return () => (long)field.GetValue(null);
  }

  // the NetworkManager only exists once the Base scene is loaded, so probes before that
  // create their own and pump ManagerUpdate() by hand
  private static FieldInfo networkManagerInstanceField;
  private static NetworkManager selfPumpInstance;
  internal static void EnsureNetworkManagerExists()
  {
    networkManagerInstanceField ??= typeof(NetworkManager).GetField(
      "Instance", BindingFlags.Static | BindingFlags.NonPublic);
    if (networkManagerInstanceField.GetValue(null) != null)
      return;

    var go = new GameObject("SLP2 NetworkManager Bootstrap");
    UnityEngine.Object.DontDestroyOnLoad(go);
    selfPumpInstance = go.AddComponent<NetworkManager>();
    selfPumpInstance.ManagerAwake();
  }

  internal static void PumpUpdate() => selfPumpInstance?.ManagerUpdate();

  internal static Func<string> ServerCodeProvider;

  internal static bool ProbePending;
  internal static Action<string> OnResponseReceived;

  internal static void SendRequest(long connectionId) =>
    Send(connectionId, ConnectionMethod.RocketNet, KindRequest, null);

  private static void SendResponse(long connectionId, ConnectionMethod method, string code) =>
    Send(connectionId, method, KindResponse, code);

  private static void Send(long connectionId, ConnectionMethod method, byte kind, string payload)
  {
    var bytes = string.IsNullOrEmpty(payload) ? [] : Encoding.UTF8.GetBytes(payload);
    if (bytes.Length > MaxPayloadSize)
    {
      Logger.Global.LogWarning($"SLP2 payload too large ({bytes.Length}b), dropping");
      return;
    }

    var data = new byte[5 + bytes.Length];
    data[0] = kind;
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(1, 4), bytes.Length);
    bytes.CopyTo(data.AsSpan(5));
    NetworkManager.SendNetworkDataDirect(connectionId, method, Channel, data);
  }

  private static bool Receive(int channel, int size, long connectionId)
  {
    Logger.Global.LogInfo($"SLP2 channel: got {size}b on channel {channel} from {connectionId}");
    if (channel != (int)Channel)
      return true;
    if (size < 5)
    {
      Logger.Global.LogWarning($"SLP2 channel: packet too short ({size}b)");
      return true;
    }

    var data = NetworkManagerBuffer.AsSpan(0, size);
    var kind = data[0];
    var length = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(1, 4));
    if (length < 0 || 5 + length > size)
    {
      Logger.Global.LogWarning($"SLP2 channel: bad payload length {length} for packet size {size}");
      return true;
    }
    var payload = length == 0 ? "" : Encoding.UTF8.GetString(data.Slice(5, length));

    switch (kind)
    {
      case KindRequest:
        var code = ServerCodeProvider?.Invoke() ?? "";
        Logger.Global.LogInfo($"SLP2 channel: request from {connectionId}, replying with {code.Length} chars");
        SendResponse(connectionId, ConnectionMethod.RocketNet, code);
        break;
      case KindResponse:
        Logger.Global.LogInfo($"SLP2 channel: response from {connectionId}, ProbePending={ProbePending}, {payload.Length} chars");
        if (ProbePending)
          OnResponseReceived?.Invoke(payload);
        break;
      default:
        Logger.Global.LogWarning($"SLP2 channel: unknown packet kind {kind}");
        break;
    }
    return true;
  }

  // insert our channel before the default-case throw so other mods can do the same
  [HarmonyPatch(typeof(NetworkManager), "ReceiveEvents"), HarmonyTranspiler]
  private static System.Collections.Generic.IEnumerable<CodeInstruction> TranspileReceiveEvents(
    System.Collections.Generic.IEnumerable<CodeInstruction> instructions, ILGenerator generator)
  {
    var matcher = new CodeMatcher(instructions, generator);

    matcher.MatchStartForward(
      new CodeMatch(inst =>
        inst.opcode == OpCodes.Call &&
        inst.operand is MethodInfo { Name: "HandleGeneralTraffic" }));
    matcher.ThrowIfInvalid("Could not find HandleGeneralTraffic call in NetworkManager.ReceiveEvents");

    matcher.MatchStartForward(
      new CodeMatch(inst =>
        inst.opcode == OpCodes.Newobj &&
        inst.operand is ConstructorInfo ctor &&
        ctor.DeclaringType == typeof(ArgumentOutOfRangeException)),
      new CodeMatch(OpCodes.Throw));
    matcher.ThrowIfInvalid(
      "Could not find throw ArgumentOutOfRangeException in NetworkManager.ReceiveEvents");

    var labels = matcher.Instruction.labels;
    matcher.Instruction.labels = [];
    matcher.CreateLabel(out var notOurs);

    matcher.InsertAndAdvance(
      new CodeInstruction(OpCodes.Ldloc_1) { labels = labels }, // channel
      new CodeInstruction(OpCodes.Ldc_I4, (int)Channel),
      new CodeInstruction(OpCodes.Bne_Un, notOurs),
      new CodeInstruction(OpCodes.Ldloc_1), // channel
      new CodeInstruction(OpCodes.Ldloc_2), // size
      new CodeInstruction(OpCodes.Ldloc_3), // connectionId
      CodeInstruction.Call(() => Receive(default, default, default)),
      new CodeInstruction(OpCodes.Ret)
    );

    return matcher.Instructions();
  }

  // probes only run without another connection, so a global flag is enough
  [HarmonyPatch(typeof(NetworkMessages.VerifyPlayerRequest), nameof(ProcessedMessage<>.Process)), HarmonyPrefix]
  private static bool SuppressAutoVerifyPlayerDuringProbe() => !ProbePending;
}
