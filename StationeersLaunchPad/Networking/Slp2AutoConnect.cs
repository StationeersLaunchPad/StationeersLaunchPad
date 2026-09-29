
using Cysharp.Threading.Tasks;

namespace StationeersLaunchPad.Networking;

// joins the server of a verified server pack once the main menu is up, same as the `join` command.
// OnGameDataLoaded is the last step of the game's startup, right after the main menu is shown
internal static class Slp2AutoConnect
{
  private static bool armed;
  private static string address;
  private static ushort port;

  // called right before StartGame() while the verify result is still fresh
  internal static void ArmIfVerified()
  {
    if (!Slp2ProfileSync.WasVerified || !Configs.ServerPacksEnabled.Value
      || !Configs.ServerPacksAutoJoin.Value)
      return;

    armed = true;
    address = Slp2ProfileSync.VerifiedAddress;
    port = Slp2ProfileSync.VerifiedPort;
    WorldManager.OnGameDataLoaded += OnMainMenuReady;
  }

  private static void OnMainMenuReady()
  {
    WorldManager.OnGameDataLoaded -= OnMainMenuReady;
    if (!armed)
      return;
    armed = false;

    DeferredJoin(address, port).Forget();
  }

  // the event fires from inside GameManager.Start(), joining right away crashes LodManager
  private static async UniTaskVoid DeferredJoin(string address, ushort port)
  {
    await UniTask.Delay(1250);
    Util.Commands.CommandLine.Process($"join {address}:{port}");
  }
}
