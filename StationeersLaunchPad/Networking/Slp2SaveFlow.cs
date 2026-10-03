
using Assets.Scripts;
using Assets.Scripts.Networking;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using StationeersLaunchPad.UI;

namespace StationeersLaunchPad.Networking;

// ProcessJoinData runs once the join was accepted, while the world is still loading
[HarmonyPatch]
internal static class Slp2SaveFlow
{
  [HarmonyPatch(typeof(NetworkClient), "ProcessJoinData"), HarmonyPrefix]
  private static void PrefixJoinData() => OfferDuringJoin().Forget();

  private static async UniTask OfferDuringJoin()
  {
    if (!Configs.ServerPacksEnabled.Value || !NetworkManager.IsClient
      || NetworkClient.ConnectionMethod != ConnectionMethod.RocketNet || !Slp2JoinMarker.ServerSharesMods)
      return;

    var attemptId = Slp2JoinMarker.AttemptId;
    var serverName = Slp2JoinMarker.ServerName;
    var address = Slp2JoinMarker.Address;
    var port = Slp2JoinMarker.Port;
    if (!Slp2ServerMatch.IsMatchableName(serverName) || string.IsNullOrEmpty(address) || port == 0)
      return;

    var profileManager = LaunchPadConfig.ProfileManager;
    if (profileManager.FindProfileByServerName(serverName) != null)
      return; // already saved for this server, nothing to offer

    // the card shows the mods, so ask for them first
    var result = await Slp2Query.Fetch(address, port);
    if (result == null)
    {
      Logger.Global.LogWarning($"{serverName} didn't send its mods, not offering its pack");
      return;
    }
    if (attemptId != Slp2JoinMarker.AttemptId || NetworkManager.NetworkState == NetworkState.Offline)
      return;

    var choice = await Slp2SaveProfilePanel.Offer(result.ServerName, result.Entries);
    if (choice == Slp2SaveChoice.None)
      return;
    if (!profileManager.SaveServerProfileFromCode(result.ServerName, result.Code, result.Entries, address, port,
      activate: choice == Slp2SaveChoice.SaveAndActivate))
      Logger.Global.LogWarning($"Failed to save server pack for '{result.ServerName}'");
  }
}
