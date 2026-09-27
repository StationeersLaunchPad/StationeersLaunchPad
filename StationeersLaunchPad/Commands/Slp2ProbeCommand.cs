
using System;
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Networking;

namespace StationeersLaunchPad.Commands;

public class Slp2ProbeCommand : SubCommand
{
  public Slp2ProbeCommand() : base("slp2probe") { }
  public override string UsageDescription => "<address[:port]> -- ask a server for its SLP2 mod code and log it";

  protected override CommandStage LeafStage => CommandStage.Init;
  protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
  {
    if (!ArgP(args).Positional(out var target).Validate())
    {
      result = null;
      return false;
    }

    if (!TryParseTarget(target, out var address, out var port))
    {
      result = $"invalid address '{target}', expected host[:port]";
      return true;
    }

    SLPCommand.AsyncCommand(RunProbe(address, port)).Forget();
    result = $"Probing {address}:{port} for SLP2 code...";
    return true;
  }

  private static bool TryParseTarget(string target, out string address, out ushort port)
  {
    address = target;
    port = 27016;
    var idx = target.LastIndexOf(':');
    if (idx == -1)
      return true;
    address = target[..idx];
    return ushort.TryParse(target[(idx + 1)..], out port);
  }

  private static async UniTask RunProbe(string address, ushort port)
  {
    var code = await Slp2ProbeClient.RequestServerCode(address, port);
    if (string.IsNullOrEmpty(code))
    {
      Logger.Global.LogWarning($"SLP2 probe of {address}:{port}: no code received (no Booster/SLP2 support, or timed out)");
      return;
    }

    Logger.Global.LogInfo($"SLP2 probe of {address}:{port} received: {code}");
    if (!Slp2PackageCode.TryDecode(code, out var mods, out var serverCode, out var serverName))
    {
      Logger.Global.LogWarning("  failed to decode received SLP2 code");
      return;
    }

    Logger.Global.LogInfo($"  decoded {mods.Count} mod(s), serverCode={serverCode}, serverName='{serverName}':");
    foreach (var mod in mods)
      Logger.Global.LogInfo($"    - {mod.Name} (ModID={mod.ModID}, workshop={mod.WorkshopHandle}, v{mod.Version})");
  }
}
