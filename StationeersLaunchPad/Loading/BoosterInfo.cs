using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StationeersLaunchPad.Metadata;

namespace StationeersLaunchPad.Loading;

// Booster ships with SLP but only does anything once a loaded mod uses it
public static class BoosterInfo
{
  private const string AssemblyName = "LaunchPadBooster";
  private static readonly byte[] ReferenceBytes = Encoding.ASCII.GetBytes(AssemblyName);
  private static readonly Dictionary<string, bool> usesBooster = new(StringComparer.OrdinalIgnoreCase);
  private static string version;

  public static string Version => version ??= ReadVersion();

  // a mod uses Booster when one of its assemblies references it
  public static bool UsedBy(ModInfo mod) => mod.Assemblies.Any(ReferencesBooster);

  private static bool ReferencesBooster(string path)
  {
    if (usesBooster.TryGetValue(path, out var uses))
      return uses;
    try
    {
      uses = Contains(File.ReadAllBytes(path), ReferenceBytes);
    }
    catch (Exception)
    {
      uses = false;
    }
    usesBooster[path] = uses;
    return uses;
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

  private static string ReadVersion()
  {
    try
    {
      var dir = Path.GetDirectoryName(typeof(BoosterInfo).Assembly.Location);
      return System.Reflection.AssemblyName.GetAssemblyName(Path.Combine(dir, $"{AssemblyName}.dll")).Version.ToString(3);
    }
    catch (Exception)
    {
      return "";
    }
  }
}
