using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Metadata;

namespace StationeersLaunchPad.UI;

public static class RefWarningPanel
{
  public static async UniTask Show(List<ModInfo> offendingMods)
  {
    if (Platform.IsServer)
      return;
    var mods = string.Join("\n", offendingMods.Select(mod => $"  {mod.Name} by {mod.About?.Author}"));
    await SlpDialog.Show(
      "Unsupported Mods",
      "The following mods contain unsupported references to StationeersLaunchPad and may break whenever "
        + "StationeersLaunchPad updates. If an error is encountered, please disable these and try again "
        + $"before reporting bugs in StationeersLaunchPad.\n\n{mods}",
      DialogTone.Warn);
  }
}
