
using System.Linq;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;

namespace StationeersLaunchPad.Networking;

public static class Slp2Handshake
{
  public static void Initialize()
  {
    Slp2Channel.ServerCodeProvider = BuildServerCode;
  }

  private static string BuildServerCode() =>
    Slp2PackageCode.Encode(
      LaunchPadConfig.ModList.EnabledMods
        .Where(mod => mod.Source != ModSourceType.Core)
        .Select(mod => new Slp2PackageCode.Entry(mod.WorkshopHandle, mod.ModID, mod.Name, mod.About?.Version ?? "")),
      serverCode: true);
}
