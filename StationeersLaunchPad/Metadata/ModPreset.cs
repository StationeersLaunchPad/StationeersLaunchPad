using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assets.Scripts.Serialization;

namespace StationeersLaunchPad.Metadata;

// A mod preset is the existing ModConfig (enabled state + load order) saved to a named XML
// file. It reuses the loader's ModConfig system: it never packages, downloads or duplicates
// mod files. Presets live in {SavePath}/presets and are for quickly switching configurations.
public static class PresetStore
{
  public static string Dir => Path.Join(LaunchPadPaths.SavePath, "presets");

  public static List<string> List()
  {
    try
    {
      if (!Directory.Exists(Dir))
        return [];
      return Directory.GetFiles(Dir, "*.xml")
        .Select(Path.GetFileNameWithoutExtension)
        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
        .ToList();
    }
    catch (Exception ex)
    {
      Logger.Global.LogWarning($"failed to list presets: {ex.Message}");
      return [];
    }
  }

  public static bool Exists(string name) => File.Exists(PathFor(name));

  public static bool Save(string name, ModConfig config)
  {
    Directory.CreateDirectory(Dir);
    return config.SaveXml(PathFor(name));
  }

  public static ModConfig Load(string name)
  {
    var path = PathFor(name);
    return File.Exists(path) ? XmlSerialization.Deserialize<ModConfig>(path) : null;
  }

  public static void Delete(string name)
  {
    try
    {
      var path = PathFor(name);
      if (File.Exists(path))
        File.Delete(path);
    }
    catch (Exception ex)
    {
      Logger.Global.LogError($"failed to delete preset '{name}': {ex.Message}");
    }
  }

  private static string PathFor(string name) =>
    Path.Join(Dir, Platform.MakeValidFileName(name.Trim()) + ".xml");
}
