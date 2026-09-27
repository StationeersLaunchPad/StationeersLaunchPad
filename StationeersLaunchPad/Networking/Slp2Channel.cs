
using System;
using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Assets.Scripts;
using Assets.Scripts.Networking;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace StationeersLaunchPad.Networking;

internal static class Slp2Channel
{
  // 239 = 'S'+'L'+'P', Booster uses 169
  internal const NetworkChannel Channel = (NetworkChannel)239;

  private const byte KindRequest = 0;
  private const byte KindResponse = 1;
  private const byte KindResponseChunk = 2;
  private static readonly Slp2CodeTransfer responseTransfer = new();
  private static long expectedResponseConnectionId = -1;

  private static byte[] networkManagerBuffer;
  private static byte[] NetworkManagerBuffer => networkManagerBuffer ??=
    (byte[])typeof(NetworkManager).GetField(
      "Buffer", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
  internal static int MaxNetworkDataBytes => NetworkManagerBuffer.Length;

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
    if (networkManagerInstanceField.GetValue(null) is NetworkManager existing && existing != null)
      return;

    var go = new GameObject("SLP2 NetworkManager Bootstrap");
    UnityEngine.Object.DontDestroyOnLoad(go);
    selfPumpInstance = go.AddComponent<NetworkManager>();
    selfPumpInstance.ManagerAwake();
  }

  internal static void PumpUpdate() => selfPumpInstance?.ManagerUpdate();

  // a leftover bootstrap manager would stay subscribed to game state changes
  internal static bool ReleaseBootstrapManager()
  {
    var bootstrap = selfPumpInstance;
    selfPumpInstance = null;
    if (bootstrap == null)
      return false;
    if (ReferenceEquals(networkManagerInstanceField.GetValue(null), bootstrap))
      networkManagerInstanceField.SetValue(null, null);
    UnityEngine.Object.Destroy(bootstrap.gameObject);
    return true;
  }

  internal static Func<string> ServerCodeProvider;

  internal static bool ProbePending;
  internal static Action<string> OnResponseReceived;

  internal static void ResetResponse()
  {
    expectedResponseConnectionId = -1;
    responseTransfer.Reset();
    OnResponseReceived = null;
  }

  internal static bool SendRequest(long connectionId)
  {
    expectedResponseConnectionId = connectionId;
    return Send(connectionId, KindRequest, []);
  }

  // used after joining when the code was too large for VerifyPlayerRequest
  internal static async UniTask<string> RequestConnectedCode(float timeoutSeconds = 5f)
  {
    if (!NetworkManager.IsClient || NetworkManager.NetworkState != NetworkState.Online
      || OnResponseReceived != null)
      return null;
    string result = null;
    var responded = false;
    ResetResponse();
    OnResponseReceived = code =>
    {
      result = code;
      responded = true;
    };
    try
    {
      if (!SendRequest(GetHostId()))
        return null;
      var deadline = Time.realtimeSinceStartup + timeoutSeconds;
      while (!responded && NetworkManager.NetworkState == NetworkState.Online
        && Time.realtimeSinceStartup < deadline)
        await UniTask.Yield();
      return result;
    }
    finally
    {
      ResetResponse();
    }
  }

  private static void SendResponse(long connectionId, string code)
  {
    var bytes = Encoding.UTF8.GetBytes(code ?? "");
    if (bytes.Length > Slp2CodeTransfer.MaxCodeBytes)
    {
      Logger.Global.LogWarning($"SLP2 code too large ({bytes.Length}b), dropping probe reply");
      return;
    }
    if (bytes.Length <= Slp2CodeTransfer.MaxFramePayload)
    {
      if (!Send(connectionId, KindResponse, bytes))
        Logger.Global.LogWarning("SLP2 probe reply could not be sent");
      return;
    }
    foreach (var chunk in Slp2CodeTransfer.Split(bytes))
      if (!Send(connectionId, KindResponseChunk, chunk))
      {
        Logger.Global.LogWarning("SLP2 probe reply stopped after a failed chunk send");
        break;
      }
  }

  private static bool Send(long connectionId, byte kind, byte[] payload)
  {
    if (payload.Length > Slp2CodeTransfer.MaxFramePayload
      || payload.Length + 5 > MaxNetworkDataBytes)
    {
      Logger.Global.LogWarning($"SLP2 frame too large ({payload.Length}b), dropping");
      return false;
    }

    var data = new byte[5 + payload.Length];
    data[0] = kind;
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(1, 4), payload.Length);
    payload.CopyTo(data.AsSpan(5));
    return NetworkManager.SendNetworkDataDirect(connectionId, ConnectionMethod.RocketNet, Channel, data);
  }

  private static bool Receive(int channel, int size, long connectionId)
  {
    Logger.Global.LogInfo($"SLP2 channel: got {size}b on channel {channel} from {connectionId}");
    if (channel != (int)Channel)
      return true;
    if (size < 5 || size > NetworkManagerBuffer.Length)
    {
      Logger.Global.LogWarning($"SLP2 channel: invalid packet size ({size}b)");
      return true;
    }

    var data = NetworkManagerBuffer.AsSpan(0, size);
    var kind = data[0];
    var length = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(1, 4));
    if (length < 0 || length > Slp2CodeTransfer.MaxFramePayload || length > size - 5)
    {
      Logger.Global.LogWarning($"SLP2 channel: bad payload length {length} for packet size {size}");
      return true;
    }
    var payload = data.Slice(5, length);

    switch (kind)
    {
      case KindRequest:
        if (!NetworkManager.IsServer || length != 0)
          break;
        string code;
        try
        {
          code = ServerCodeProvider?.Invoke() ?? "";
        }
        catch (Exception ex)
        {
          Logger.Global.LogWarning("SLP2 channel: failed to build server code for probe reply");
          Logger.Global.LogException(ex);
          code = "";
        }
        Logger.Global.LogInfo($"SLP2 channel: request from {connectionId}, replying with {code.Length} chars");
        SendResponse(connectionId, code);
        break;
      case KindResponse:
        Logger.Global.LogInfo($"SLP2 channel: response from {connectionId}, ProbePending={ProbePending}, {payload.Length}b");
        if (OnResponseReceived != null && connectionId == expectedResponseConnectionId)
        {
          responseTransfer.Reset();
          OnResponseReceived?.Invoke(Encoding.UTF8.GetString(payload));
        }
        break;
      case KindResponseChunk:
        if (OnResponseReceived != null && connectionId == expectedResponseConnectionId
          && responseTransfer.TryAccept(connectionId, payload, out var completedCode))
          OnResponseReceived?.Invoke(completedCode);
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
