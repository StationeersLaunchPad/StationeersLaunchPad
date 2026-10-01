using Assets.Scripts;
using Assets.Scripts.Networking;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.UI;

namespace StationeersLaunchPad.Networking;

// ProcessJoinData runs once the join was accepted, while the world is still loading
[HarmonyPatch]
internal static class Slp2SaveFlow
{
  [HarmonyPatch(typeof(NetworkClient), "ProcessJoinData"), HarmonyPrefix]
  private static void PrefixJoinData() => OfferDuringJoin().Forget();

  internal static void CancelPending() => Slp2SaveProfilePanel.CancelSuccessOffer();

  private static async UniTask OfferDuringJoin()
  {
    if (!Configs.ServerPacksEnabled.Value || !NetworkManager.IsClient
      || NetworkClient.ConnectionMethod != ConnectionMethod.RocketNet)
      return;

    var attemptId = Slp2JoinPiggyback.AttemptId;
    var code = Slp2JoinPiggyback.LastReceivedCode;
    var needsChannelFetch = Slp2JoinPiggyback.NeedsChannelFetch;
    var address = NetworkClient.Address;
    if (string.IsNullOrEmpty(address) || !ushort.TryParse(NetworkClient.Port, out var port) || port == 0)
      return;

    if (code == null && needsChannelFetch)
      code = await Slp2Channel.RequestConnectedCode();
    if (attemptId != Slp2JoinPiggyback.AttemptId || NetworkManager.NetworkState == NetworkState.Offline)
      return;
    if (!Slp2PackageCode.TryDecode(code, out var entries, out var isServerCode, out var serverName)
      || !isServerCode || !Slp2ServerMatch.IsMatchableName(serverName))
      return;

    var profileManager = LaunchPadConfig.ProfileManager;
    if (profileManager.FindProfileByServerName(serverName) != null)
      return; // already saved for this server, nothing to offer

    if (!await Slp2SaveProfilePanel.Offer(serverName))
      return;

    if (!profileManager.SaveServerProfileFromCode(serverName, code, entries, address, port, activate: false))
      Logger.Global.LogWarning($"Failed to save server pack for '{serverName}'");
  }
}
