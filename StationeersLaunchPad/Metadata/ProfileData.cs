using System.Collections.Generic;
using System.ComponentModel;
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

  // set for server packs
  [XmlAttribute("ServerName")]
  [DefaultValue("")]
  public string ServerName = "";

  // last SLP2 code received from the server
  [XmlAttribute("ServerCode")]
  [DefaultValue("")]
  public string ServerCode = "";

  // fallback for servers that aren't in the server list
  [XmlAttribute("LastAddress")]
  [DefaultValue("")]
  public string LastAddress = "";

  [XmlAttribute("LastPort")]
  [DefaultValue((ushort)0)]
  public ushort LastPort;

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
  [DefaultValue(true)]
  public bool LegacyEnabled = true;
}
