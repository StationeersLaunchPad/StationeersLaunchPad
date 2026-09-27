
using Assets.Scripts;
using Assets.Scripts.Networking;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.UI;

namespace StationeersLaunchPad.Networking;

// offers to save the server's mods as a pack when a join fails after we received its SLP2 code
[HarmonyPatch]
internal static class Slp2JoinFailureOffer
{
  private static bool reachedGame;
  private static bool offered;

  internal static void Initialize() => NetworkClient.ClientFinishedJoining += () =>
  {
    reachedGame = true;
    Slp2SaveProfilePanel.OnJoinFinished();
  };

  // first message of every connection attempt
  [HarmonyPatch(typeof(NetworkMessages.VerifyPlayerRequest), "Deserialize"), HarmonyPostfix]
  private static void PostfixNewAttempt()
  {
    reachedGame = false;
    offered = false;
  }

  // can run twice for one failed attempt, and also runs when a probe disconnects
  [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.EndConnection)), HarmonyPostfix]
  private static void PostfixEndConnection()
  {
    Slp2SaveFlow.CancelPending();
    var code = Slp2JoinPiggyback.TakeReceivedCode();
    if (Slp2Channel.ProbePending)
      return;

    if (!Configs.ServerProfilesEnabled.Value || code == null || reachedGame || offered
      || NetworkClient.ConnectionMethod != ConnectionMethod.RocketNet)
      return;
    offered = true;

    Offer(NetworkClient.Address, NetworkClient.Port, code).Forget();
  }

  private static async UniTask Offer(string address, string port, string code)
  {
    if (string.IsNullOrEmpty(address) || !ushort.TryParse(port, out var portNum) || portNum == 0
      || !Slp2PackageCode.TryDecode(code, out var entries, out var isServerCode, out var serverName)
      || !isServerCode || !Slp2ServerMatch.IsMatchableName(serverName))
      return;

    var profileManager = LaunchPadConfig.ProfileManager;
    if (profileManager.FindProfileByServerName(serverName)?.ServerCode == code)
      return; // already have exactly this list saved - saving again fixes nothing

    if (!await Slp2SaveProfilePanel.OfferMismatch(serverName))
      return;

    if (!profileManager.SaveServerProfileFromCode(serverName, code, entries, address, portNum))
      Logger.Global.LogWarning($"Failed to save server profile for '{serverName}'");
  }
}
