
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace StationeersLaunchPad.Repos
{
  [XmlRoot("ModRepos")]
  public class ModReposConfig
  {
    [XmlElement("GitHub", typeof(GitHubRepoDef))]
    [XmlElement("Http", typeof(HttpRepoDef))]
    public List<ModRepoDef> Repos = new();

    [XmlElement("RepoMod", typeof(RepoModDef))]
    public List<RepoModDef> Mods = new();
  }

  public struct RepoFetchResult
  {
    public byte[] Data;
    public bool UseCache;
    public string CacheKey;
  }
  public abstract class ModRepoDef
  {
    [XmlAttribute("DirName")] public string DirName;
    [XmlAttribute("LastFetch")] public DateTime LastFetch;
    [XmlAttribute("Digest")] public string Digest;

    [XmlIgnore]
    public ModRepoData Data;

    public string LocalDirPath => Path.Join(LaunchPadPaths.ModReposPath, DirName);
    public string LocalDataPath => Path.Join(LocalDirPath, "modrepo.xml");

    public abstract string ID { get; }

    public abstract UniTask<RepoFetchResult> FetchRemote();
    public abstract void SetCacheKey(string cacheKey);
    public abstract bool HasCacheKey { get; }
  }

  public class RepoModDef
  {
    [XmlAttribute("ModID")] public string ModID;
    [XmlAttribute("Branch")] public string Branch;
    [XmlAttribute("MinVersion")] public string MinVersion;
    [XmlAttribute("MaxVersion")] public string MaxVersion;
    [XmlAttribute("Repo")] public string RepoID;

    [XmlAttribute("Version")] public string Version;
    [XmlAttribute("DirName")] public string DirName;

    [XmlIgnore] public string PrevDirName;
    [XmlIgnore] public ModRepoDef Repo;

    public string DisplayName =>
      $"{ModID}@{Branch}[{Version}({MinVersion},{MaxVersion})] from {RepoID}";
  }

  public class RepoModUpdateTarget
  {
    public RepoModDef Mod;
    public ModVersionData Version;
    public string DirName;
  }
}