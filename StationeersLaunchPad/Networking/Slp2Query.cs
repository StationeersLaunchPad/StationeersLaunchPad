
using System.Collections.Generic;
using System.Threading;
using Assets.Scripts;
using Assets.Scripts.Networking;
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Metadata;

namespace StationeersLaunchPad.Networking;

// asks a server for the mods it shares. on that server already it asks over the connection,
// otherwise it opens a short one that never joins
internal static class Slp2Query
{
  internal sealed class Result(string code, List<Slp2PackageCode.Entry> entries, string serverName)
  {
    internal readonly string Code = code;
    internal readonly List<Slp2PackageCode.Entry> Entries = entries;
    internal readonly string ServerName = serverName;
  }

  // null when the server didn't answer or doesn't share its mods
  internal static async UniTask<Result> Fetch(string address, ushort port, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrEmpty(address) || port == 0)
      return null;
    var connected = NetworkManager.IsClient && NetworkManager.NetworkState == NetworkState.Online
      && address == NetworkClient.Address && port.ToString() == NetworkClient.Port;
    var code = connected
      ? await Slp2Channel.RequestConnectedCode()
      : await Slp2ProbeClient.RequestServerCode(address, port, cancellationToken: cancellationToken);
    if (!Slp2PackageCode.TryDecode(code, out var entries, out var isServerCode, out var serverName) || !isServerCode)
      return null;
    return new(code, entries, serverName);
  }
}
