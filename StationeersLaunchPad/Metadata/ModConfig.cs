
using Assets.Scripts.Serialization;
using System.IO;

namespace StationeersLaunchPad.Metadata
{
  public static class ModConfigUtil
  {
    public static ModConfig LoadConfig()
    {
      var config = File.Exists(LaunchPadPaths.ConfigPath)
            ? XmlSerialization.Deserialize<ModConfig>(LaunchPadPaths.ConfigPath) ?? new()
            : new ModConfig();
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