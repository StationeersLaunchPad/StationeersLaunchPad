using System.Linq;
using Assets.Scripts.Serialization;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;

namespace StationeersLaunchPad.Networking;

public static class Slp2Handshake
{
  private static bool warnedName;
  private static bool warnedLocal;

  public static void Initialize()
  {
    Slp2Channel.ServerCodeProvider = BuildServerCode;
  }

  // disabled servers still answer with an empty code so probes don't time out
  private static string BuildServerCode()
  {
    if (!Configs.ShareModList.Value)
      return "";

    // players can only get workshop mods, a list with anything else can't be loaded
    var mods = LaunchPadConfig.ModList.EnabledMods.Where(mod => mod.Source != ModSourceType.Core).ToList();
    var local = mods.FirstOrDefault(mod => mod.WorkshopHandle <= 1);
    if (local != null)
    {
      if (!warnedLocal)
      {
        warnedLocal = true;
        Logger.Global.LogWarning($"SLP2: not sharing the mod list, '{local.Name}' isn't on the workshop");
      }
      return "";
    }

    var serverName = Settings.CurrentData.ServerName;
    if (!warnedName && !Slp2ServerMatch.IsMatchableName(serverName))
    {
      warnedName = true;
      Logger.Global.LogWarning($"SLP2: '{serverName}' is a default server name, players won't be offered to save this server's mods as a pack");
    }

    return Slp2PackageCode.Encode(
      mods.Select(mod => new Slp2PackageCode.Entry(mod.WorkshopHandle, mod.ModID, mod.Name, mod.About?.Version ?? "")),
      serverCode: true,
      serverName: serverName);
  }
}
