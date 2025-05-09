using System.Collections.Generic;
using System.Xml;
using System.Xml.Serialization;
using Assets.Scripts.Networking.Transports;
using Steamworks.Ugc;

namespace StationeersLaunchPad
{
  public class ModInfo
  {
    public int SortIndex;
    public bool DepsWarned;

    public ModSource Source;
    public SteamTransport.ItemWrapper Wrapped;
    public Item WorkshopItem;
    public bool Enabled;

    public ModAbout About;

    public string Path => this.Source == ModSource.Core ? "" : this.Wrapped.DirectoryPath;

    public List<AssemblyInfo> Assemblies = new();
    public List<string> AssetBundles = new();

    public LoadedMod Loaded;

    public string DisplayName
    {
      get
      {
        if (this.Source == ModSource.Core) return "Core";
        if (this.About == null)
          return this.Path;
        return this.About.Name;
      }
    }

    public ulong WorkshopHandle
    {
      get
      {
        if (this.Source == ModSource.Core) return 1;
        if (this.About == null) return ulong.MaxValue;
        return this.About.WorkshopHandle;
      }
    }

    public bool SortBefore(ModInfo other)
    {
      if (other.About?.LoadBefore?.Find(v => v.Id == this.WorkshopHandle) != null)
        return true;
      var selfAfter = this.About?.LoadAfter;
      if (this.About?.LoadAfter?.Find(v => v.Id == other.WorkshopHandle) != null)
        return true;
      return false;
    }
  }

  public enum ModSource
  {
    Core,
    Local,
    Workshop
  }

  [XmlRoot("ModMetadata")]
  public class ModAbout
  {
    [XmlElement]
    public string Name;
    [XmlElement]
    public string Author;
    [XmlElement]
    public string Version;
    [XmlElement]
    public string Description;
    [XmlElement(IsNullable = true)]
    [XmlIgnore]
    private string _inGameDescription;
    [XmlElement("InGameDescription", IsNullable = true)]
    public XmlCDataSection InGameDescription
    {
      get => new XmlDocument().CreateCDataSection(this._inGameDescription);
      set => this._inGameDescription = value.Value;
    }
    public string ChangeLog;
    [XmlElement]
    public ulong WorkshopHandle;
    [XmlArray("Tags"), XmlArrayItem("Tag")]
    public List<string> Tags;

    [XmlArray("Dependencies"), XmlArrayItem("Mod")]
    public List<ModVersion> Dependencies;
    [XmlArray("LoadBefore"), XmlArrayItem("Mod")]
    public List<ModVersion> LoadBefore;
    [XmlArray("LoadAfter"), XmlArrayItem("Mod")]
    public List<ModVersion> LoadAfter;
  }

  [XmlRoot("Mod")]
  public class ModVersion
  {
    [XmlElement]
    public string Version;
    [XmlElement]
    public ulong Id;
  }
}