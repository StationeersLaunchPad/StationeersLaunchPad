
using System;
using Assets.Scripts.Networking;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StationeersLaunchPad.Networking;

public static class Slp2ProbeClient
{
  public static async UniTask<string> RequestServerCode(
    string address, ushort port, float timeoutSeconds = 10f)
  {
    if (NetworkManager.NetworkState != NetworkState.Offline)
    {
      Logger.Global.LogWarning("SLP2 probe refused: a network connection is already active");
      return null;
    }

    LaunchPadConfig.PauseAutoWait();
    Slp2Channel.EnsureNetworkManagerExists();

    string result = null;
    var responded = false;
    Slp2Channel.OnResponseReceived = code =>
    {
      result = code;
      responded = true;
    };
    Slp2Channel.ProbePending = true;

    try
    {
      bool started;
      try
      {
        started = NetworkManager.StartClient(address, port, 0);
      }
      catch (Exception ex)
      {
        Logger.Global.LogWarning("SLP2 probe failed to start: game networking isn't ready yet at this point in startup");
        Logger.Global.LogException(ex);
        return null;
      }
      if (!started)
      {
        Logger.Global.LogWarning("SLP2 probe: StartClient returned false");
        return null;
      }

      var connected = false;
      var lastLoggedState = NetworkManager.NetworkState;
      var deadline = Time.realtimeSinceStartup + timeoutSeconds;
      Logger.Global.LogInfo($"SLP2 probe: connecting, initial state {lastLoggedState}");
      while (!responded && Time.realtimeSinceStartup < deadline)
      {
        Slp2Channel.PumpUpdate();
        if (NetworkManager.NetworkState != lastLoggedState)
        {
          lastLoggedState = NetworkManager.NetworkState;
          Logger.Global.LogInfo($"SLP2 probe: state changed to {lastLoggedState}");
        }
        if (!connected && NetworkManager.NetworkState == NetworkState.Online)
        {
          connected = true;
          var hostId = Slp2Channel.GetHostId();
          Logger.Global.LogInfo($"SLP2 probe: connected (hostId={hostId}), sending request on channel {(int)Slp2Channel.Channel}");
          Slp2Channel.SendRequest(hostId);
        }
        if (NetworkManager.NetworkState == NetworkState.Offline)
        {
          Logger.Global.LogWarning("SLP2 probe: connection dropped/rejected before a response arrived");
          break;
        }
        await UniTask.Yield();
      }

      if (!responded)
        Logger.Global.LogWarning($"SLP2 probe: timed out (connected={connected}, finalState={NetworkManager.NetworkState})");

      return result;
    }
    finally
    {
      Slp2Channel.ProbePending = false;
      Slp2Channel.OnResponseReceived = null;
      if (NetworkManager.NetworkState != NetworkState.Offline)
        NetworkManager.EndConnection();
    }
  }
}
