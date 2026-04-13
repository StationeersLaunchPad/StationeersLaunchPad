using System.Collections.Generic;
using System.Xml.Serialization;

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
    [XmlAttribute("DirectoryPath")]
    public string DirectoryPath;
    
    [XmlAttribute("WorkshopHandle")]
    public ulong WorkshopHandle;
    
    [XmlAttribute("Enabled")]
    public bool Enabled;
}