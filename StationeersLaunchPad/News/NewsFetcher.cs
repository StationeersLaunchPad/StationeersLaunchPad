using System;
using System.IO;
using System.Xml.Serialization;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;

namespace StationeersLaunchPad.News;

public static class NewsFetcher
{
  private static readonly XmlSerializer Serializer = new(typeof(NewsFeed));

  public static async UniTask<NewsFeed> Fetch(string url)
  {
    if (string.IsNullOrWhiteSpace(url))
      return null;

    try
    {
      using var request = UnityWebRequest.Get(url);
      request.timeout = Configs.NewsFetchTimeout.Value;

      Logger.Global.LogDebug($"Fetching news {url}");
      var result = await request.SendWebRequest();

      if (result.result != UnityWebRequest.Result.Success)
      {
        Logger.Global.LogError($"Failed to fetch news {url}. result: {result.result}, error: {result.error}");
        return null;
      }

      using var reader = new StringReader(result.downloadHandler.text);
      var feed = (NewsFeed)Serializer.Deserialize(reader) ?? new();
      Logger.Global.LogDebug($"News: fetched {feed.Entries?.Count ?? 0} entries from {url}");
      return feed;
    }
    catch (Exception ex)
    {
      Logger.Global.LogException(ex);
      Logger.Global.LogError($"Failed to fetch news {url}. Skipping");
      return null;
    }
  }
}
