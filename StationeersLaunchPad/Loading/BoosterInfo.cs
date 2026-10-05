using System;
using System.IO;

namespace StationeersLaunchPad.Loading;

// Booster ships with SLP but only does anything once a loaded mod uses it
public static class BoosterInfo
{
  private static string version;

  public static string Version => version ??= ReadVersion();

  private static string ReadVersion()
  {
    try
    {
      var dir = Path.GetDirectoryName(typeof(BoosterInfo).Assembly.Location);
      return System.Reflection.AssemblyName.GetAssemblyName(Path.Combine(dir, "LaunchPadBooster.dll")).Version.ToString(3);
    }
    catch (Exception)
    {
      return "";
    }
  }
}
