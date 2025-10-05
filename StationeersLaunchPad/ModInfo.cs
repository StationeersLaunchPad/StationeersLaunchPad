using Assets.Scripts.Networking.Transports;
using Steamworks.Ugc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;

namespace StationeersLaunchPad
{
  public class ModInfo
  {
    public const int MOD_NAME_SIZE_LIMIT = 128;
    public const int MOD_DESCRIPTION_SIZE_LIMIT = 8000;
    public const int MOD_CHANGELOG_SIZE_LIMIT = 8000;
    public const int MOD_THUMBNAIL_SIZE_LIMIT = 1024 * 1024;

    public int SortIndex;
    public bool DepsWarned;

    public ModSource Source;
    public SteamTransport.ItemWrapper Wrapped;
    public Item WorkshopItem;
    public bool Enabled;

    public ModAbout About;

    public string Name => this.Wrapped.DirectoryName;
    public string Path => this.Source == ModSource.Core ? LaunchPadPaths.ManagedPath : this.Wrapped.DirectoryPath;
    public string AboutPath => System.IO.Path.Combine(this.Path, "About");
    public string AboutXmlPath => System.IO.Path.Combine(this.AboutPath, "About.xml");
    public string ThumbnailPath => System.IO.Path.Combine(this.AboutPath, "thumb.png");
    public string PreviewPath => System.IO.Path.Combine(this.AboutPath, "preview.png");

    public List<AssemblyInfo> Assemblies = [];
    public List<string> AssetBundles = [];

    public LoadedMod Loaded;

    public string DisplayName => this.About == null ? this.Name : this.About.Name;

    public ulong WorkshopHandle => this.Source switch
    {
      ModSource.Core => 1,
      ModSource.Workshop => this.Wrapped.Id,
      _ => this.About?.WorkshopHandle ?? 0,
    };

    public bool SortBefore(ModInfo other)
      => other.About?.LoadBefore?.Find(v => v.Id == this.WorkshopHandle) != null || this.About?.LoadAfter?.Find(v => v.Id == other.WorkshopHandle) != null;

    public (bool, string) IsWorkshopValid()
    {
      // if its core its fine, if its on the workshop in the first place its probably fine too
      if (this.Source != ModSource.Local)
        return (true, string.Empty);

      if (this.About == null)
        return (false, "Mod has invalid/no about data.");

      if (this.About.Name?.Length > ModInfo.MOD_NAME_SIZE_LIMIT)
        return (false, $"Mod name is larger than {ModInfo.MOD_NAME_SIZE_LIMIT} characters, current size is {this.About.Name?.Length} characters.");

      if (this.About.Description?.Length > ModInfo.MOD_DESCRIPTION_SIZE_LIMIT)
        return (false, $"Mod description is larger than {ModInfo.MOD_DESCRIPTION_SIZE_LIMIT} characters, current size is {this.About.Description?.Length} characters.");

      if (this.About.ChangeLog?.Length > ModInfo.MOD_CHANGELOG_SIZE_LIMIT)
        return (false, $"Mod changelog is larger than {ModInfo.MOD_CHANGELOG_SIZE_LIMIT} characters, current size is {this.About.ChangeLog?.Length} characters.");

      if (!File.Exists(this.ThumbnailPath))
        return (false, $"Mod does not have a thumb.png in the About folder.");

      var thumbnailInfo = new FileInfo(this.ThumbnailPath);
      if (thumbnailInfo?.Length > ModInfo.MOD_THUMBNAIL_SIZE_LIMIT)
        return (false, $"Mod thumbnail size is larger than {ModInfo.MOD_THUMBNAIL_SIZE_LIMIT / 1024} kilobytes, current size is {thumbnailInfo?.Length / 1024} kilobytes.");

      return (true, string.Empty);
    }

    public void OpenLocalFolder()
    {
      try
      {
        Process.Start("explorer.exe", $"\"{this.Path}\"");
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
      }
    }

    public void OpenWorkshopPage()
    {
      try
      {
        Application.OpenURL($"steam://url/CommunityFilePage/{this.WorkshopHandle}");
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
      }
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

    [XmlIgnore]
    private string _inGameDescription;

    [XmlElement("InGameDescription", IsNullable = true)]
    public XmlCDataSection InGameDescription
    {
      get => !string.IsNullOrEmpty(this._inGameDescription) ? new XmlDocument().CreateCDataSection(this._inGameDescription) : null;
      set => this._inGameDescription = value?.Value;
    }

    [XmlElement(IsNullable = true)]
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