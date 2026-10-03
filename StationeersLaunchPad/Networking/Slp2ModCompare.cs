
using System.Collections.Generic;
using System.Linq;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;

namespace StationeersLaunchPad.Networking;

internal enum Slp2ModState { Match, NotInstalled, NotEnabled, Newer, Older, NotOnServer }

internal readonly struct Slp2ModRow(ulong workshopHandle, string name, string serverVersion, string localVersion, Slp2ModState state)
{
  internal readonly ulong WorkshopHandle = workshopHandle;
  internal readonly string Name = name;
  internal readonly string ServerVersion = serverVersion;
  internal readonly string LocalVersion = localVersion;
  internal readonly Slp2ModState State = state;
}

// the server's mods next to what this client is about to load
internal static class Slp2ModCompare
{
  internal static List<Slp2ModRow> Compare(
    IEnumerable<Slp2PackageCode.Entry> serverMods, ModList modList, ProfileManager profileManager)
  {
    var rows = new List<Slp2ModRow>();
    var handles = new HashSet<ulong>();
    foreach (var entry in serverMods)
    {
      handles.Add(entry.WorkshopHandle);
      var serverVersion = entry.Version?.Trim() ?? "";
      // a local copy of a workshop mod has the same handle
      var local = modList.AllMods
        .Where(mod => mod.WorkshopHandle == entry.WorkshopHandle)
        .OrderByDescending(mod => mod.Enabled)
        .FirstOrDefault();
      if (local == null)
      {
        rows.Add(new(entry.WorkshopHandle, entry.Name, serverVersion, "", Slp2ModState.NotInstalled));
        continue;
      }
      var localVersion = local.About?.Version?.Trim() ?? "";
      rows.Add(new(entry.WorkshopHandle, local.Name ?? entry.Name, serverVersion, localVersion,
        !local.Enabled ? Slp2ModState.NotEnabled : CompareVersions(localVersion, serverVersion)));
    }

    foreach (var mod in modList.EnabledMods)
      if (mod.Source != ModSourceType.Core && !handles.Contains(mod.WorkshopHandle) && !profileManager.IsClientside(mod))
        rows.Add(new(mod.WorkshopHandle, mod.Name, "", mod.About?.Version?.Trim() ?? "", Slp2ModState.NotOnServer));
    return rows;
  }

  internal static bool AllMatch(List<Slp2ModRow> rows) => rows.All(row => row.State == Slp2ModState.Match);

  // a mod without a version can't be compared, so it counts as matching
  private static Slp2ModState CompareVersions(string local, string server)
  {
    if (local.Length == 0 || server.Length == 0)
      return Slp2ModState.Match;
    var cmp = StationeersLaunchPad.Version.Compare(local, server);
    return cmp > 0 ? Slp2ModState.Newer : cmp < 0 ? Slp2ModState.Older : Slp2ModState.Match;
  }
}
