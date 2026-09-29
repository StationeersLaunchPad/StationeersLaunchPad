using System.Linq;
using Assets.Scripts.Serialization;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;

namespace StationeersLaunchPad.Networking;

public static class Slp2Handshake
{
  private static bool warnedName;

  public static void Initialize()
  {
    Slp2Channel.ServerCodeProvider = BuildServerCode;
  }

  // disabled servers still answer with an empty code so probes don't time out
  private static string BuildServerCode()
  {
    if (!Configs.ShareModList.Value)
      return "";

    var serverName = Settings.CurrentData.ServerName;
    if (!warnedName && !Slp2ServerMatch.IsMatchableName(serverName))
    {
      warnedName = true;
      Logger.Global.LogWarning($"SLP2: '{serverName}' is a default server name, players won't be offered to save this server's mods as a pack");
    }

    return Slp2PackageCode.Encode(
      LaunchPadConfig.ModList.EnabledMods
        .Where(mod => mod.Source != ModSourceType.Core)
        .Select(mod => new Slp2PackageCode.Entry(mod.WorkshopHandle, mod.ModID, mod.Name, mod.About?.Version ?? "")),
      serverCode: true,
      serverName: serverName);
  }
}
