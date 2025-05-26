using Assets.Scripts;
using BepInEx;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace StationeersLaunchPad
{
  // similar to jixxed's updater
  public static class LaunchPadUpdater
  {
    public static bool AllowUpdate;

    public const string LatestReleaseUrl = "https://api.github.com/repos/StationeersLaunchPad/StationeersLaunchPad/releases/latest";
    public const string GithubUrl = "https://github.com/StationeersLaunchPad/StationeersLaunchPad";

    public static string TemporaryPath => Path.GetTempPath();
    public static string ExtractionPath => Path.Combine(TemporaryPath, "StationeersLaunchPad");

    public static List<string> Assemblies = new()
    {
      "RG.ImGui",
      "StationeersMods.Interface",
      "StationeersMods.Shared",
      "StationeersLaunchPad",
    };

    public static async UniTask CheckVersion()
    {
      var latestRelease = await FetchLatestRelease();
      // If we failed to get a release for whatever reason, just bail
      if (latestRelease == null)
        return;

      var latestVersion = new Version(latestRelease.TagName.TrimStart('V', 'v'));
      var currentVersion = new Version(LaunchPadPlugin.pluginVersion.TrimStart('V', 'v'));

      if (latestVersion <= currentVersion)
      {
        Logger.Global.Log($"Plugin is up-to-date.");
        return;
      }

      Logger.Global.LogWarning($"Plugin is NOT up-to-date.");

      await CheckShouldUpdate(latestRelease);
      if (!AllowUpdate)
        return;

      try
      {
        await PerformUpdate(latestRelease);

        LaunchPadConfig.HasUpdated = true;
        Logger.Global.LogError($"Mod loader has been updated to version {latestVersion}, please restart your game!");
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
        Logger.Global.LogError("An error occurred during update. Rolling back");
        RevertUpdate();
      }
    }

    private static async UniTask<Github.Release> FetchLatestRelease()
    {
      try
      {
        using (var request = UnityWebRequest.Get(LaunchPadUpdater.LatestReleaseUrl))
        {
          request.timeout = 10; // 10 seconds because we are only fetching a json file

          Logger.Global.LogDebug($"Requesting version...");
          UnityWebRequest result = await request.SendWebRequest();

          if (result.result != UnityWebRequest.Result.Success)
          {
            Logger.Global.LogError($"Failed to send web request! result: {result.result}, error: {result.error}");
            return null;
          }

          return JsonConvert.DeserializeObject<Github.Release>(result.downloadHandler.text);
        }
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
        Logger.Global.LogError("Failed to fetch latest release info. Skipping update");
        return null;
      }
    }

    private static async UniTask CheckShouldUpdate(Github.Release release)
    {
      if (LaunchPadConfig.AutoUpdate)
      {
        AllowUpdate = true;
        return;
      }

      var description = Github.FormatDescription(release.Description);
      await LaunchPadAlertGUI.Show("Update Available", $"Update version {release.TagName} is available, would you like to automatically download and update?\n\n{description}",
        new Vector2(800, 400),
        LaunchPadAlertGUI.DefaultPosition,
        ("Yes", () => {
          AllowUpdate = true; return true;
        }),
        ("Open GitHub", () => {
          AllowUpdate = false;
          Application.OpenURL(release.HtmlUrl);
          return false;
        }),
        ("No", () => {
          AllowUpdate = false; return true;
        })
      );
    }

    private static async UniTask ExtractDownloadArchive(Github.Release release)
    {
      var assetName = GameManager.IsBatchMode ? $"StationeersLaunchPad-server-{release.TagName}.zip" : $"StationeersLaunchPad-{release.TagName}.zip";
      var asset = release.Assets.Find(a => a.Name == assetName);
      if (asset == null)
      {
        Logger.Global.LogError($"Failed to find {assetName} in release. Skipping update");
        return;
      }

      if (Directory.Exists(LaunchPadUpdater.ExtractionPath))
      {
        foreach (var file in Directory.GetFiles(LaunchPadUpdater.ExtractionPath))
        {
          var path = Path.Combine(LaunchPadUpdater.ExtractionPath, file);

          if (File.Exists(path))
          {
            File.Delete(path);
          }
        }
        Directory.Delete(LaunchPadUpdater.ExtractionPath);
      }

      var zipFilePath = Path.Combine(LaunchPadUpdater.TemporaryPath, assetName);
      if (File.Exists(zipFilePath))
      {
        File.Delete(zipFilePath);
      }

      using (var downloadRequest = UnityWebRequest.Get(asset.BrowserDownloadUrl))
      {
        downloadRequest.timeout = 45; // max of 45 seconds to download the zip file

        Logger.Global.LogDebug($"Requesting download file...");
        UnityWebRequest downloadResult = await downloadRequest.SendWebRequest();

        if (downloadResult.result != UnityWebRequest.Result.Success)
        {
          Logger.Global.LogError($"Failed to send web request to download! result: {downloadResult.result}, error: {downloadResult.error}");
          return;
        }

        Logger.Global.LogDebug($"Writing file to {zipFilePath}...");
        File.WriteAllBytes(zipFilePath, downloadResult.downloadHandler.data);
      }

      using (var zipFile = ZipFile.Open(zipFilePath, ZipArchiveMode.Read))
      {
        Logger.Global.LogDebug($"Extracting file contents to {LaunchPadUpdater.ExtractionPath}...");
        zipFile.ExtractToDirectory(LaunchPadUpdater.TemporaryPath);
      }
      File.Delete(zipFilePath);
    }

    private static async UniTask PerformUpdate(Github.Release release)
    {
      await ExtractDownloadArchive(release);

      Logger.Global.LogDebug($"Extracted file contents to {LaunchPadUpdater.ExtractionPath}!");
      if (!Directory.Exists(LaunchPadUpdater.ExtractionPath))
      {
        Logger.Global.LogError($"Failed to exteract zip file");
        return;
      }

      var pluginPath = Path.Combine(Paths.PluginPath, "StationeersLaunchPad");
      foreach (var file in LaunchPadUpdater.Assemblies)
      {
        var fileName = $"{file}.dll";
        var backupFileName = $"{file}.dll.bak";
        var newPath = Path.Combine(LaunchPadUpdater.ExtractionPath, fileName);
        if (!File.Exists(newPath))
        {
          continue;
        }

        var path = Path.Combine(pluginPath, fileName);
        if (!File.Exists(path))
        {
          continue;
        }

        var backupPath = Path.Combine(pluginPath, backupFileName);
        if (File.Exists(backupPath))
        {
          File.Delete(backupPath);
        }

        Logger.Global.LogDebug($"Backing up DLL to {backupPath}!");
        File.Move(path, backupPath);
        Logger.Global.LogDebug($"Deleting DLL at {path}!");
        File.Delete(path);

        Logger.Global.LogDebug($"Moving new DLL to {newPath}!");
        File.Move(newPath, path);
      }
      Directory.Delete(LaunchPadUpdater.ExtractionPath);
    }

    private static void RevertUpdate()
    {
      try
      {
        var pluginPath = Path.Combine(Paths.PluginPath, "StationeersLaunchPad");
        foreach (var file in LaunchPadUpdater.Assemblies)
        {
          var fileName = $"{file}.dll";
          var backupFileName = $"{file}.dll.bak";

          var backupPath = Path.Combine(pluginPath, backupFileName);
          if (!File.Exists(backupPath))
          {
            continue;
          }

          var path = Path.Combine(pluginPath, fileName);
          if (File.Exists(path))
          {
            File.Delete(path);
          }

          Logger.Global.LogDebug($"Moving backup DLL to {path}!");
          File.Move(backupPath, path);
        }

        Logger.Global.LogWarning($"Mod loader has reverted update changes due to an error.");
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
        Logger.Global.LogError("Error rolling back update. StationeersLaunchPad install may be in an invalid state.");
      }
    }

    public class Github
    {
      public class Release
      {
        [JsonProperty("name")]
        public string Name;

        [JsonProperty("tag_name")]
        public string TagName;

        [JsonProperty("target_commitish")]
        public string BranchName;

        [JsonProperty("id")]
        public int Id;

        [JsonProperty("body")]
        public string Description;

        [JsonProperty("author")]
        public User Author;

        [JsonProperty("assets")]
        public List<Asset> Assets;

        [JsonProperty("draft")]
        public bool Draft;

        [JsonProperty("prerelease")]
        public bool Prerelease;

        [JsonProperty("created_at")]
        public DateTime Created;

        [JsonProperty("uploaded_at")]
        public DateTime Uploaded;

        [JsonProperty("mentions_count")]
        public int UsersMentioned;

        [JsonProperty("url")]
        public string Url;

        [JsonProperty("assets_url")]
        public string AssetsUrl;

        [JsonProperty("upload_url")]
        public string UploadUrl;

        [JsonProperty("html_url")]
        public string HtmlUrl;

        [JsonProperty("tarball_url")]
        public string TarballUrl;

        [JsonProperty("zipball_url")]
        public string ZipballUrl;
      }


      public class Asset
      {
        [JsonProperty("name")]
        public string Name;

        [JsonProperty("id")]
        public int Id;

        [JsonProperty("size")]
        public int Size;

        [JsonProperty("content_type")]
        public string Type;

        [JsonProperty("uploader")]
        public User Uploader;

        [JsonProperty("created_at")]
        public DateTime Created;

        [JsonProperty("updated_at")]
        public DateTime Updated;

        [JsonProperty("download_count")]
        public int DownloadCount;

        [JsonProperty("label")]
        public string Label;

        [JsonProperty("state")]
        public string State;

        [JsonProperty("url")]
        public string Url;

        [JsonProperty("digest")]
        public string Digest;

        [JsonProperty("browser_download_url")]
        public string BrowserDownloadUrl;
      }

      public class User
      {
        [JsonProperty("login")]
        public string Name;

        [JsonProperty("id")]
        public int Id;

        [JsonProperty("type")]
        public string Type;

        [JsonProperty("url")]
        public string Url;

        [JsonProperty("avatar_url")]
        public string AvatarUrl;

        [JsonProperty("html_url")]
        public string HtmlUrl;
      }

      public static string FormatDescription(string description)
      {
        var text = description
          .Replace($"{LaunchPadUpdater.GithubUrl}/pull/", "#")
          .Replace($"{LaunchPadUpdater.GithubUrl}/compare/", "")
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
}
