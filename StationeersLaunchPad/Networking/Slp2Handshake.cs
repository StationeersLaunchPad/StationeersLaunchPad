
using System.Collections.Generic;
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

  // null when this server doesn't share its mods
  internal static string SharedServerName() =>
    SharedMods() == null ? null : Slp2ServerMatch.CleanName(Settings.CurrentData.ServerName);

  private static List<ModInfo> SharedMods()
  {
    if (!Configs.ShareModList.Value)
      return null;

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
      return null;
    }
    return mods;
  }

  // disabled servers still answer with an empty code so queries don't time out
  private static string BuildServerCode()
  {
    var mods = SharedMods();
    if (mods == null)
      return "";

    var serverName = Slp2ServerMatch.CleanName(Settings.CurrentData.ServerName);
    if (!warnedName && !Slp2ServerMatch.IsMatchableName(serverName))
    {
      warnedName = true;
      Logger.Global.LogWarning($"SLP2: '{serverName}' is a default server name, players won't be offered to save this server's mods as a pack");
    }

    return Slp2PackageCode.Encode(
      mods.Select(mod => new Slp2PackageCode.Entry(mod.WorkshopHandle, mod.Name, mod.About?.Version ?? "")),
      serverCode: true,
      serverName: serverName);
  }
}
