using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine.Networking;

namespace StationeersLaunchPad
{
  public class Github
  {
    public static readonly Repo LaunchPadRepo = new("StationeersLaunchPad", "StationeersLaunchPad");

    private const string UserPattern = @"\w+(?:\-\w+)*";
    private const string RepoPattern = @"[\w\.\-]+";
    private static readonly Regex PullPrefixRegex = new($"https://github.com/({UserPattern})/({RepoPattern})/pull/");
    private static readonly Regex ComparePrefixRegex = new($"https://github.com/({UserPattern})/({RepoPattern})/compare/");
    public static readonly Regex RepoRegex =
      new($"^(?:https?://)?github.com/({UserPattern})/({RepoPattern})/?$", RegexOptions.IgnoreCase);

    private static async UniTask<ZipArchive> FetchZipToMemory(Asset asset)
    {
      using (var downloadRequest = UnityWebRequest.Get(asset.BrowserDownloadUrl))
      {
        downloadRequest.timeout = Configs.UpdateDownloadTimeout.Value;

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

    private static async UniTask<T> FetchJSON<T>(string url) where T : class
    {
      try
      {
        using (var request = UnityWebRequest.Get(url))
        {
          request.timeout = Configs.UpdateCheckTimeout.Value;

          Logger.Global.LogDebug($"Fetching {url}");
          var result = await request.SendWebRequest();

          if (result.result != UnityWebRequest.Result.Success)
          {
            Logger.Global.LogError($"Failed to fetch {url}. result: {result.result}, error: {result.error}");
            return null;
          }

          return JsonConvert.DeserializeObject<T>(result.downloadHandler.text);
        }
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
        Logger.Global.LogError($"Failed to fetch {url}. Skipping update");
        return null;
      }
    }

    public class Repo
    {
      public readonly string Owner;
      public readonly string Name;

      public Repo(string owner, string name)
      {
        this.Owner = owner;
        this.Name = name;
      }

      public string WebURL => $"https://github.com/{Owner}/{Name}";
      public string ReleaseListURL => $"https://api.github.com/repos/{Owner}/{Name}/releases";
      public string LatestReleaseURL => $"{this.ReleaseListURL}/latest";
      public string TagReleaseURL(string tag) => $"{this.ReleaseListURL}/tags/{tag}";

      public async UniTask<List<Release>> FetchReleaseList()
      {
        var releases = await FetchJSON<List<Release>>(this.ReleaseListURL) ?? new();
        foreach (var release in releases)
          this.InitRelease(release);
        return releases;
      }

      public async UniTask<Release> FetchLatestRelease()
      {
        var release = await FetchJSON<Release>(this.LatestReleaseURL);
        InitRelease(release);
        return release;
      }

      public async UniTask<Release> FetchTagRelease(string tag)
      {
        var release = await FetchJSON<Release>(this.TagReleaseURL(tag));
        InitRelease(release);
        return release;
      }

      private void InitRelease(Release release)
      {
        if (release == null)
          return;
        release.Repo = this;
        if (release.Assets != null)
        {
          foreach (var asset in release.Assets)
            asset.Release = release;
        }
      }
    }

    public class Release
    {
      [JsonIgnore] public Repo Repo;
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

      public string FormatDescription()
      {
        var text = Description;
        var matches = PullPrefixRegex.Matches(text);
        for (var i = 0; i < matches.Count; i++)
        {
          var match = matches[i];
          var relName = this.RelativeName(match.Groups[1].Value, match.Groups[2].Value);
          text = text.Replace(match.Value, $"{relName}#");
        }
        matches = ComparePrefixRegex.Matches(text);
        for (var i = 0; i < matches.Count; i++)
        {
          var match = matches[i];
          var relName = this.RelativeName(match.Groups[1].Value, match.Groups[2].Value);
          text = text.Replace(match.Value, relName);
        }
        var lines = text
          .TrimEnd('v', 'V', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.')
          .Split('\n')
          .ToList();

        var desc = "";
        for (var i = 1; i < lines.Count - 2; i++)
        {
          desc += lines[i] + "\n";
        }

        return $"Whats Changed?\n{desc}";
      }

      private string RelativeName(string owner, string name) => (owner, name) switch
      {
        _ when owner == Repo.Owner && name == Repo.Name => "",
        _ when owner == Repo.Owner => name,
        _ => $"{owner}/{name}",
      };
    }

    public class Asset
    {
      [JsonIgnore] public Release Release;
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

      public UniTask<ZipArchive> FetchToMemory() => FetchZipToMemory(this);
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
  }
}