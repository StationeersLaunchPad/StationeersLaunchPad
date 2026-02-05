
using Cysharp.Threading.Tasks;
using System.Xml.Serialization;

namespace StationeersLaunchPad.Repos
{
  public class GitHubRepoDef : ModRepoDef
  {
    [XmlAttribute("Owner")] public string Owner;
    [XmlAttribute("Name")] public string Name;
    [XmlAttribute("ETag")] public string ETag;

    [XmlIgnore]
    public override string ID => $"github.com/{Owner}/{Name}";

    public override UniTask<RepoFetchResult> FetchRemote() => HttpRepoDef.FetchHttp(
      $"https://raw.githubusercontent.com/{Owner}/{Name}/refs/heads/modrepo/modrepo.xml",
      ETag
    );
    public override void SetCacheKey(string cacheKey) => ETag = cacheKey;
  }
}