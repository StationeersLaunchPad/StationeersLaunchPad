
using System.Collections.Generic;
using System.Xml.Serialization;

namespace StationeersLaunchPad.Repos
{
  [XmlRoot("ModRepo")]
  public class ModRepoData
  {
    [XmlElement("ModVersion", typeof(ModVersionData))]
    public List<ModVersionData> ModVersions = new();
  }

  public class ModVersionData
  {
    [XmlAttribute("ModID")] public string ModID;
    [XmlAttribute("Version")] public string Version;
    [XmlAttribute("Name")] public string Name;
    [XmlAttribute("Author")] public string Author;
    [XmlAttribute("Url")] public string Url;
    [XmlAttribute("Digest")] public string Digest;

    [XmlElement("Branch")]
    public List<StringData> Branches = new();
    [XmlElement("Tag")]
    public List<StringData> Tags = new();
    [XmlElement("Dep")]
    public List<ModVersionDepData> Deps = new();
  }

  public class StringData
  {
    [XmlAttribute("Value")] public string Value;

    public override string ToString() => Value;

    public static implicit operator StringData(string value) =>
      new() { Value = value };
    public static implicit operator string(StringData data) =>
      data.Value;
  }

  public class ModVersionDepData
  {
    [XmlAttribute("ModID")]
    public string ModID;
  }
}