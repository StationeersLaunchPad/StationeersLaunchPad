using System;
using System.IO;
using System.Xml.Serialization;
using Assets.Scripts.Serialization;

namespace StationeersLaunchPad.UI;

// User-adjustable, persisted layout: mod-list width and console height fraction.
[XmlRoot("LaunchPadLayout")]
public class LayoutPrefs
{
  [XmlElement("ListWidth")]
  public float ListWidth = 360f;

  [XmlElement("ConsoleFraction")]
  public float ConsoleFraction = 0.20f;

  private static LayoutPrefs current;
  private static string Path => System.IO.Path.Join(LaunchPadPaths.SavePath, "ui_layout.xml");

  public static LayoutPrefs Current
  {
    get
    {
      if (current == null)
      {
        try
        {
          if (File.Exists(Path))
            current = XmlSerialization.Deserialize<LayoutPrefs>(Path);
        }
        catch (Exception ex)
        {
          Logger.Global.LogWarning($"failed to read {Path}: {ex.Message}");
        }
        current ??= new LayoutPrefs();
      }
      return current;
    }
  }

  public static void Save()
  {
    try
    {
      if (!Current.SaveXml(Path))
        Logger.Global.LogWarning($"failed to save {Path}");
    }
    catch (Exception ex)
    {
      Logger.Global.LogWarning($"failed to save {Path}: {ex.Message}");
    }
  }
}
