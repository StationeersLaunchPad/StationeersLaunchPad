using System.Collections.Generic;
using System.Xml.Serialization;
using StationeersLaunchPad.Sources;

namespace StationeersLaunchPad.Metadata;

[XmlRoot("ModProfile")]
public class ProfileData
{
  [XmlAttribute("Name")]
  public string Name;

  [XmlAttribute("Description")]
  public string Description = "";

  [XmlElement("Mod")]
  public List<ProfileModEntry> Mods = [];
}

public class ProfileModEntry
{
  [XmlAttribute("Name")]
  public string Name = "";

  [XmlAttribute("Source")]
  public ModSourceType Source;

  [XmlAttribute("DirectoryPath")]
  public string DirectoryPath;

  [XmlAttribute("WorkshopHandle")]
  public ulong WorkshopHandle;

  [XmlAttribute("ModID")]
  public string ModID;

  [XmlAttribute("Enabled")]
  public bool Enabled;
}
