
using Cysharp.Threading.Tasks;
using System;
using System.Xml.Serialization;
using UnityEngine.Networking;

namespace StationeersLaunchPad.Repos
{
  public class HttpRepoDef : ModRepoDef
  {
    [XmlAttribute("Url")] public string Url;
    [XmlAttribute("ETag")] public string ETag;

    [XmlIgnore]
    public override string ID => Url;

    public override UniTask<RepoFetchResult> FetchRemote() => FetchHttp(Url, ETag);
    public override void SetCacheKey(string cacheKey) => ETag = cacheKey;
    public override bool HasCacheKey => !string.IsNullOrEmpty(ETag);

    public static async UniTask<RepoFetchResult> FetchHttp(string url, string etag)
    {
      Logger.Global.LogDebug($"Fetching {url}");
      try
      {
        using var req = UnityWebRequest.Get(url);
        req.timeout = Configs.RepoFetchTimeout.Value;
        if (!string.IsNullOrEmpty(etag))
          req.SetRequestHeader("If-None-Match", etag);

        var res = await req.SendWebRequest();
        if (res.result != UnityWebRequest.Result.Success)
        {
          Logger.Global.LogError($"Failed to fetch {url}: {res.error}");
          return new();
        }

        if (res.responseCode == 304)
          return new() { UseCache = true };

        if (res.responseCode != 200)
        {
          Logger.Global.LogError($"Failed to fetch {url}: status {res.responseCode}");
          return new();
        }

        return new()
        {
          Data = res.downloadHandler.data,
          CacheKey = res.GetResponseHeader("ETag"),
        };
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
        return new();
      }
    }
  }
}