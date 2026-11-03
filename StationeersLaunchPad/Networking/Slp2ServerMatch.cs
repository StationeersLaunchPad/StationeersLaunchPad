
using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Networking;
using Cysharp.Threading.Tasks;

namespace StationeersLaunchPad.Networking;

internal static class Slp2ServerMatch
{
  // default names would match unrelated servers
  private static readonly string[] NameBlocklist =
  [
    "Stationeers",
    "Stationeers Server UI",
    "Stationeers Server",
  ];

  internal static bool IsMatchableName(string name) =>
    !string.IsNullOrWhiteSpace(name) &&
    !NameBlocklist.Any(blocked => blocked.Equals(name, StringComparison.OrdinalIgnoreCase));

  // servers whose pack offer got a "No thanks". a line each, names can hold any other separator
  internal static HashSet<string> Declined() =>
    new((Configs.DeclinedServers?.Value ?? "").Split('\n').Select(name => name.Trim()).Where(name => name.Length > 0),
      StringComparer.OrdinalIgnoreCase);

  internal static bool IsDeclined(string name) => Declined().Contains(name);

  internal static void Decline(string name)
  {
    var declined = Declined();
    if (declined.Add(name))
      Configs.DeclinedServers.Value = string.Join("\n", declined.OrderBy(server => server, StringComparer.OrdinalIgnoreCase));
  }

  internal static void ForgetDeclined() => Configs.DeclinedServers.Value = "";

  // links in server names (usually a discord invite) don't belong in pack names
  internal static string CleanName(string name)
  {
    if (string.IsNullOrEmpty(name))
      return "";
    var cut = new[] { "https://", "http://" }
      .Select(link => name.IndexOf(link, StringComparison.OrdinalIgnoreCase))
      .Where(index => index >= 0)
      .DefaultIfEmpty(name.Length)
      .Min();
    return name[..cut].Trim();
  }

  // returns null unless exactly one listed server has this name
  internal static async UniTask<GameSession> FindByName(string serverName)
  {
    if (!IsMatchableName(serverName))
      return null;
    try
    {
      await NetworkManager.GetGameSessionList();
    }
    catch (Exception ex)
    {
      Logger.Global.LogWarning($"SLP2 server list unavailable: {ex.Message}");
      return null;
    }
    var matches = NetworkManager.GameSessionList
      .Where(session => serverName.Equals(CleanName(session.Name), StringComparison.OrdinalIgnoreCase))
      .ToList();
    return matches.Count == 1 ? matches[0] : null;
  }
}
