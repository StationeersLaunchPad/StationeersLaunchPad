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
      
      try
      {
        var result = await request.SendWebRequest();
      }
      catch (Exception ex) when (ex.Message.Contains("resolve") || ex.Message.Contains("host"))
      {
        // No internet connection or DNS resolution failed
        Logger.Global.LogInfo($"No internet connection or DNS resolution failed for {url}");
        return null;
      }

      // Check result after successful request
      if (request.result != UnityWebRequest.Result.Success)
      {
        Logger.Global.LogInfo($"Failed to fetch news {url}. result: {request.result}");
        return null;
      }

      using var reader = new StringReader(request.downloadHandler.text);
      var feed = (NewsFeed)Serializer.Deserialize(reader) ?? new();
      Logger.Global.LogDebug($"News: fetched {feed.Entries?.Count ?? 0} entries from {url}");
      return feed;
    }
    catch (Exception ex)
    {
      // Catch any other unexpected exceptions
      Logger.Global.LogDebug($"Failed to fetch news {url}: {ex.Message}");
      return null;
    }
  }
}