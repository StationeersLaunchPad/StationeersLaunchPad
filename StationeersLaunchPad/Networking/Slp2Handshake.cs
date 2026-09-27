
using System.Linq;
using Assets.Scripts.Serialization;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;

namespace StationeersLaunchPad.Networking;

public static class Slp2Handshake
{
  public static void Initialize()
  {
    Slp2Channel.ServerCodeProvider = BuildServerCode;
  }

  // disabled servers still answer with an empty code so probes don't time out
  private static string BuildServerCode() => !Configs.ServerProfilesEnabled.Value ? "" :
    Slp2PackageCode.Encode(
      LaunchPadConfig.ModList.EnabledMods
        .Where(mod => mod.Source != ModSourceType.Core)
        .Select(mod => new Slp2PackageCode.Entry(mod.WorkshopHandle, mod.ModID, mod.Name, mod.About?.Version ?? "")),
      serverCode: true,
      serverName: Settings.CurrentData.ServerName);
}
