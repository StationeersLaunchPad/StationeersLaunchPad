using Assets.Scripts.Serialization;
using BepInEx;
using UnityEngine;

namespace StationeersLaunchPad
{
  public static class LaunchPadPaths
  {
    public static string ExecutablePath => Paths.ExecutablePath;
    public static string GameRootPath => Paths.GameRootPath;
    public static string ManagedPath => Paths.ManagedPath;
    public static string PluginPath => Paths.PluginPath;
    public static string StreamingAssetsPath => Application.streamingAssetsPath;
    public static string SavePath => string.IsNullOrEmpty(Settings.CurrentData.SavePath) ? Settings.CurrentData.SavePath : StationSaveUtils.DefaultPath;
    public static string ConfigPath => WorkshopMenu.ConfigPath;
  }
}
