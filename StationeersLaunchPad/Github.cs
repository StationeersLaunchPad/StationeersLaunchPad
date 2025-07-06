using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine.Networking;

namespace StationeersLaunchPad
{
  public class Github
  {
    public const string RepoUrl = "https://github.com/StationeersLaunchPad/StationeersLaunchPad";
    public const string ReleaseUrlBase = "https://api.github.com/repos/StationeersLaunchPad/StationeersLaunchPad/releases";
    public static string TagReleaseUrl(string tag) => $"{ReleaseUrlBase}/tags/{tag}";
    public static string LatestReleaseUrl => $"{ReleaseUrlBase}/latest";

    public static UniTask<Release> FetchLatestRelease() => FetchRelease("latest", LatestReleaseUrl);
    public static UniTask<Release> FetchTagRelease(string version) => FetchRelease(version, TagReleaseUrl(version));

    private static async UniTask<Release> FetchRelease(string version, string url)
    {
      try
      {
        using (var request = UnityWebRequest.Get(url))
        {
          request.timeout = 10; // 10 seconds because we are only fetching a json file

          Logger.Global.LogDebug($"Fetching {version} release info");
          var result = await request.SendWebRequest();

          if (result.result != UnityWebRequest.Result.Success)
          {
            Logger.Global.LogError($"Failed to fetch {version} release info! result: {result.result}, error: {result.error}");
            return null;
          }

          return JsonConvert.DeserializeObject<Release>(result.downloadHandler.text);
        }
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
        Logger.Global.LogError($"Failed to fetch {version} release info. Skipping update");
        return null;
      }
    }

    public static async UniTask<ZipArchive> FetchZipArchive(Asset asset)
    {
      using (var downloadRequest = UnityWebRequest.Get(asset.BrowserDownloadUrl))
      {
        downloadRequest.timeout = 45; // max of 45 seconds to download the zip file

        Logger.Global.LogDebug($"Downloading {asset.Name}");
        var downloadResult = await downloadRequest.SendWebRequest();

        if (downloadResult.result != UnityWebRequest.Result.Success)
        {
          Logger.Global.LogError($"Failed to download {asset.Name}! result: {downloadResult.result}, error: {downloadResult.error}");
          return null;
        }

        var data = downloadRequest.downloadHandler.data;
        Logger.Global.LogDebug($"Downloaded {data.Length} bytes");
        var stream = new MemoryStream(data.Length);
        stream.Write(data, 0, data.Length);
        stream.Position = 0;

        return new ZipArchive(stream);
      }
    }

    public class Release
    {
      [JsonProperty("name")] public string Name;
      [JsonProperty("tag_name")] public string TagName;
      [JsonProperty("target_commitish")] public string BranchName;
      [JsonProperty("id")] public int Id;
      [JsonProperty("body")] public string Description;
      [JsonProperty("author")] public User Author;
      [JsonProperty("assets")] public List<Asset> Assets;
      [JsonProperty("draft")] public bool Draft;
      [JsonProperty("prerelease")] public bool Prerelease;
      [JsonProperty("created_at")] public DateTime Created;
      [JsonProperty("uploaded_at")] public DateTime Uploaded;
      [JsonProperty("mentions_count")] public int UsersMentioned;
      [JsonProperty("url")] public string Url;
      [JsonProperty("assets_url")] public string AssetsUrl;
      [JsonProperty("upload_url")] public string UploadUrl;
      [JsonProperty("html_url")] public string HtmlUrl;
      [JsonProperty("tarball_url")] public string TarballUrl;
      [JsonProperty("zipball_url")] public string ZipballUrl;
    }

    public class Asset
    {
      [JsonProperty("name")] public string Name;
      [JsonProperty("id")] public int Id;
      [JsonProperty("size")] public int Size;
      [JsonProperty("content_type")] public string Type;
      [JsonProperty("uploader")] public User Uploader;
      [JsonProperty("created_at")] public DateTime Created;
      [JsonProperty("updated_at")] public DateTime Updated;
      [JsonProperty("download_count")] public int DownloadCount;
      [JsonProperty("label")] public string Label;
      [JsonProperty("state")] public string State;
      [JsonProperty("url")] public string Url;
      [JsonProperty("digest")] public string Digest;
      [JsonProperty("browser_download_url")] public string BrowserDownloadUrl;
    }

    public class User
    {
      [JsonProperty("login")] public string Name;
      [JsonProperty("id")] public int Id;
      [JsonProperty("type")] public string Type;
      [JsonProperty("url")] public string Url;
      [JsonProperty("avatar_url")] public string AvatarUrl;
      [JsonProperty("html_url")] public string HtmlUrl;
    }

    public static string FormatDescription(string description)
    {
      var text = description
        .Replace($"{RepoUrl}/pull/", "#")
        .Replace($"{RepoUrl}/compare/", "")
        .TrimEnd('v', 'V', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.')
        .Split('\n')
        .ToList();

      var desc = "";
      for (var i = 1; i < text.Count - 2; i++)
      {
        desc += text[i] + "\n";
      }

      return $"Whats Changed?\n{desc}";
    }
  }
}