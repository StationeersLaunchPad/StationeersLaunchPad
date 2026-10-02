using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StationeersLaunchPad.Metadata;

namespace StationeersLaunchPad.Loading;

// cheap guesses from a mod's files without loading them: each assembly is read once and
// searched for a few names that end up in its metadata
public static class ModScan
{
  private const string Booster = "LaunchPadBooster";
  // new prefabs, custom network messages and save data only work when the server has the mod too
  private static readonly string[] ContentMarkers = ["AddPrefabs", "SourcePrefabs"];
  private static readonly string[] NetworkMarkers =
    ["INetworkMessage", "INetworkRPC", "ModNetworkMessage", "RegisterNetworkMessage", "RegisterRPC"];
  private static readonly string[] SaveMarkers = ["AddSaveDataType"];
  private static readonly string[] Markers = [Booster, .. ContentMarkers, .. NetworkMarkers, .. SaveMarkers];
  private static readonly Dictionary<string, HashSet<string>> found = new(StringComparer.OrdinalIgnoreCase);

  public static bool UsesBooster(ModInfo mod) => mod.Assemblies.Any(path => Mentions(path, Booster));

  // the author's ModSide when set, otherwise what the files suggest. guess is false when SLP is sure
  public static (ModSide side, string label, bool guess, string reason) Side(ModInfo mod)
  {
    if (mod.About?.ModSide is { } side && side != ModSide.Unknown)
      return (side, side switch
      {
        ModSide.Client => "Clientside",
        ModSide.Server => "Server only",
        _ => "Client and server",
      }, false, "Set by the mod's author.");
    if (Any(mod, NetworkMarkers))
      return (ModSide.Both, "Needs the server", false, "It sends its own network messages, so the server must have it too.");
    if (Any(mod, ContentMarkers))
      return (ModSide.Both, "Probably needs the server", true, "It adds things to the game, so the server needs it too.");
    if (Any(mod, SaveMarkers))
      return (ModSide.Both, "Probably needs the server", true, "It stores its own save data, so the server needs it too.");
    return (ModSide.Unknown, "Potentially clientside", true, "SLP found nothing that needs the server, but can't be sure.");
  }

  private static bool Any(ModInfo mod, string[] markers) =>
    mod.Assemblies.Any(path => markers.Any(marker => Mentions(path, marker)));

  private static bool Mentions(string path, string marker)
  {
    if (!found.TryGetValue(path, out var markers))
      found[path] = markers = Scan(path);
    return markers.Contains(marker);
  }

  private static HashSet<string> Scan(string path)
  {
    var markers = new HashSet<string>();
    try
    {
      var data = File.ReadAllBytes(path);
      foreach (var marker in Markers)
        if (Contains(data, Encoding.ASCII.GetBytes(marker)))
          markers.Add(marker);
    }
    catch (Exception)
    {
      // unreadable assemblies just count as mentioning nothing
    }
    return markers;
  }

  private static bool Contains(byte[] data, byte[] pattern)
  {
    for (var i = 0; i <= data.Length - pattern.Length; i++)
    {
      var j = 0;
      while (j < pattern.Length && data[i + j] == pattern[j])
        j++;
      if (j == pattern.Length)
        return true;
    }
    return false;
  }
}
