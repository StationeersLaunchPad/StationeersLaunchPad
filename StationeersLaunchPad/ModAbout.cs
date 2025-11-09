
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace StationeersLaunchPad
{
  [XmlRoot("ModMetadata")]
  public class ModAbout
  {
    [XmlElement("Name")]
    public string Name;

    [XmlElement("Guid")]
    public string Guid;

    [XmlElement("Author")]
    public string Author;

    [XmlElement("Version")]
    public string Version;

    [XmlElement("Description")]
    public string Description;

    [XmlIgnore]
    public string InGameDescription;

    [XmlElement("InGameDescription", IsNullable = true)]
    public XmlCDataSection InGameDescriptionCData
    {
      get => !string.IsNullOrEmpty(this.InGameDescription) ? new XmlDocument().CreateCDataSection(this.InGameDescription) : null;
      set => this.InGameDescription = value?.Value;
    }

    [XmlElement("ChangeLog", IsNullable = true)]
    public string ChangeLog;

    [XmlElement("WorkshopHandle")]
    public ulong WorkshopHandle;

    [XmlArray("Tags"), XmlArrayItem("Tag")]
    public List<string> Tags;

    [XmlElement("DependsOn")]
    public List<ModReference> DependsOn;

    [XmlElement("OrderBefore")]
    public List<ModReference> OrderBefore;

    [XmlElement("OrderAfter")]
    public List<ModReference> OrderAfter;

    // Fields below are old fields whose name and/or format changed. They just proxy to the replacements above

    [XmlArray("Dependencies"), XmlArrayItem("Mod")]
    public List<ModReference> _Legacy_Dependencies
    {
      get => DependsOn; set => DependsOn = value;
    }
    public bool ShouldSerialize_Legacy_Dependencies() => false;

    [XmlArray("LoadBefore"), XmlArrayItem("Mod")]
    public List<ModReference> _Legacy_LoadBefore
    {
      get => OrderAfter; set => OrderAfter = value;
    }
    public bool ShouldSerialize_Legacy_LoadBefore() => false;

    [XmlArray("LoadAfter"), XmlArrayItem("Mod")]
    public List<ModReference> _Legacy_LoadAfter
    {
      get => OrderBefore; set => OrderBefore = value;
    }
    public bool ShouldSerialize_Legacy_LoadAfter() => false;
  }

  [XmlRoot("Mod")]
  public class ModReference
  {
    [XmlAttribute("Guid")]
    public string Guid;

    [XmlAttribute("WorkshopHandle")]
    public ulong WorkshopHandle;

    [XmlAttribute("Version")]
    public string Version;

    public override string ToString()
    {
      if (!this.IsValid)
        return "Invalid";
      var sb = new StringBuilder();
      if (!string.IsNullOrEmpty(this.Guid))
        sb.AppendFormat("Guid: {0}", this.Guid);
      if (WorkshopHandle != 0)
        sb.AppendFormat("{0}WorkshopHandle: {1}", sb.Length == 0 ? "" : ", ", this.WorkshopHandle);
      if (!string.IsNullOrEmpty(this.Version))
        sb.AppendFormat("{0}Version: {1}", sb.Length == 0 ? "" : ", ", this.Version);
      return sb.ToString();
    }

    public bool IsValid => WorkshopHandle != 0 || !string.IsNullOrEmpty(Guid);

    // Fields below are old fields whose name and/or format changed. They just proxy to the replacements above

    [XmlElement("Id")]
    public ulong _Legacy_Id
    {
      get => WorkshopHandle; set => WorkshopHandle = value;
    }
    public bool ShouldSerialize_Legacy_Id() => false;

    [XmlAttribute("Id")]
    public ulong _Legacy_IdAttr
    {
      get => WorkshopHandle; set => WorkshopHandle = value;
    }
    public bool ShouldSerialize_Legacy_IdAttr() => false;

    [XmlElement("Version")]
    public string _Legacy_Version
    {
      get => Version; set => Version = value;
    }
    public bool ShouldSerialize_Legacy_Version() => false;
  }
}