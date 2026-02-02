using Assets.Scripts.Serialization;
using BepInEx;
using System.IO;
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
    public static string SavePath => string.IsNullOrEmpty(Settings.CurrentData.SavePath) ? StationSaveUtils.DefaultPath : Settings.CurrentData.SavePath;
    public static string ConfigPath => WorkshopMenu.ConfigPath;

    public static string ModReposConfigPath =>
      Path.Join(StationSaveUtils.DefaultPath, "modrepos.xml");
    public static string ModReposPath =>
      Path.Join(StationSaveUtils.DefaultPath, "modrepos");
    public static string RepoModsPath =>
      Path.Join(StationSaveUtils.DefaultPath, "repomods");

    private static DirectoryInfo _cachedInstallDir;
    public static DirectoryInfo InstallDir
    {
      get
      {
        if (_cachedInstallDir == null)
        {
          var dir = Directory.GetParent(typeof(LaunchPadPaths).Assembly.Location);
          if (dir == null || !dir.Exists)
            return null;

          var pluginDir = new DirectoryInfo(PluginPath);
          var parent = dir;
          var nested = false;
          // ensure install path is inside bepinex plugins
          while (parent != null)
          {
            if (parent.FullName == pluginDir.FullName)
            {
              nested = true;
              break;
            }
            parent = parent.Parent;
          }
          if (!nested)
            return null;
          _cachedInstallDir = dir;
        }
        return _cachedInstallDir;
      }
    }
  }
}
