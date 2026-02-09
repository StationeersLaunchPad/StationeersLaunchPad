
using Assets.Scripts.Serialization;
using System.IO;

namespace StationeersLaunchPad.Metadata
{
  public static class ModConfigUtil
  {
    public static ModConfig LoadConfig()
    {
      var path = LaunchPadPaths.ConfigPath;
      ModConfig config = null;
      if (File.Exists(path))
      {
        config = XmlSerialization.Deserialize<ModConfig>(path);
        if (config == null)
          Logger.Global.LogWarning($"Replacing invalid modconfig at {path}");
      }
      config ??= new();
      config.CreateCoreMod();
      return config;
    }

    public static void SaveConfig(ModConfig config)
    {
      if (!config.SaveXml(LaunchPadPaths.ConfigPath))
        Logger.Global.LogError($"failed to save {LaunchPadPaths.ConfigPath}");
    }
  }
}