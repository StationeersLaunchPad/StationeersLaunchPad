
using System;
using System.Threading;
using Assets.Scripts.Networking;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StationeersLaunchPad.Networking;

public static class Slp2ProbeClient
{
  public static async UniTask<string> RequestServerCode(
    string address, ushort port, float timeoutSeconds = 10f,
    CancellationToken cancellationToken = default)
  {
    if (!Configs.ServerProfilesEnabled.Value)
    {
      Logger.Global.LogInfo("SLP2 probe skipped: server profiles are disabled");
      return null;
    }
    if (Slp2Channel.ProbePending || NetworkManager.NetworkState != NetworkState.Offline)
    {
      Logger.Global.LogWarning("SLP2 probe refused: a network connection is already active");
      return null;
    }

    LaunchPadConfig.PauseAutoWait();
    Slp2Channel.EnsureNetworkManagerExists();
    Slp2Channel.ResetResponse();

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
      while (!responded && !cancellationToken.IsCancellationRequested
        && Time.realtimeSinceStartup < deadline)
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
          if (!Slp2Channel.SendRequest(hostId))
          {
            Logger.Global.LogWarning("SLP2 probe request could not be sent");
            break;
          }
        }
        if (NetworkManager.NetworkState == NetworkState.Offline)
        {
          Logger.Global.LogWarning("SLP2 probe: connection dropped/rejected before a response arrived");
          break;
        }
        await UniTask.Yield();
      }

      if (!responded && !cancellationToken.IsCancellationRequested)
        Logger.Global.LogWarning($"SLP2 probe: timed out (connected={connected}, finalState={NetworkManager.NetworkState})");

      return cancellationToken.IsCancellationRequested ? null : result;
    }
    finally
    {
      // keep ProbePending set so EndConnection hooks can ignore probes
      try
      {
        if (NetworkManager.NetworkState != NetworkState.Offline)
          NetworkManager.EndConnection();
      }
      finally
      {
        try
        {
          // Destroy is deferred, wait for OnDestroy before the real manager connects
          if (Slp2Channel.ReleaseBootstrapManager())
            await UniTask.Yield();
        }
        finally
        {
          // a dropped probe can be offline without EndConnection running
          Slp2JoinPiggyback.TakeReceivedCode();
          Slp2Channel.ResetResponse();
          Slp2Channel.ProbePending = false;
        }
      }
    }
  }
}
