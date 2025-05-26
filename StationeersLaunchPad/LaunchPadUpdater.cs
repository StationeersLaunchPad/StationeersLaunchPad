using Assets.Scripts;
using BepInEx;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Networking;

namespace StationeersLaunchPad
{
  // similar to jixxed's updater
  public static class LaunchPadUpdater
  {
    public static bool AllowUpdate;

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

    private static async UniTask<GithubRelease> FetchLatestRelease()
    {
      try
      {
        using (var request = UnityWebRequest.Get("https://api.github.com/repos/StationeersLaunchPad/StationeersLaunchPad/releases/latest"))
        {
          request.timeout = 10; // 10 seconds because we are only fetching a json file

          Logger.Global.LogDebug($"Requesting version...");
          UnityWebRequest result = await request.SendWebRequest();

          if (result.result != UnityWebRequest.Result.Success)
          {
            Logger.Global.LogError($"Failed to send web request! result: {result.result}, error: {result.error}");
            return null;
          }

          return JsonConvert.DeserializeObject<GithubRelease>(result.downloadHandler.text);
        }
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
        Logger.Global.LogError("Failed to fetch latest release info. Skipping update");
        return null;
      }
    }

    private static async UniTask CheckShouldUpdate(GithubRelease release)
    {
      if (LaunchPadConfig.AutoUpdate)
      {
        AllowUpdate = true;
        return;
      }

      await LaunchPadAlertGUI.Show("Update Available", "An update is available, would you like to automatically download and update?",
        LaunchPadAlertGUI.DefaultSize,
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
          AllowUpdate = false;
          return true;
        })
      );
    }

    private static async UniTask PerformUpdate(GithubRelease release)
    {
      var assetName = GameManager.IsBatchMode ? $"StationeersLaunchPad-server-{release.TagName}.zip" : $"StationeersLaunchPad-{release.TagName}.zip";
      var asset = release.Assets.Find(a => a.Name == assetName);
      if (asset == null)
      {
        Logger.Global.LogError($"Failed to find {assetName} in release. Skipping update");
        return;
      }

      var tempPath = Path.GetTempPath();
      var extractionPath = Path.Combine(tempPath, "StationeersLaunchPad");
      if (Directory.Exists(extractionPath))
      {
        foreach (var file in Directory.GetFiles(extractionPath))
        {
          var path = Path.Combine(extractionPath, file);

          if (File.Exists(path))
          {
            File.Delete(path);
          }
        }
        Directory.Delete(extractionPath);
      }
      var zipFilePath = Path.Combine(tempPath, "SLP.zip");
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
        Logger.Global.LogDebug($"Extracting file contents to {extractionPath}...");
        zipFile.ExtractToDirectory(tempPath);
      }
      File.Delete(zipFilePath);

      Logger.Global.LogDebug($"Extracted file contents to {extractionPath}!");
      if (!Directory.Exists(extractionPath))
      {
        Logger.Global.LogError($"Failed to exteract zip file");
        return;
      }

      var pluginPath = Path.Combine(Paths.PluginPath, "StationeersLaunchPad");
      foreach (var file in LaunchPadUpdater.Assemblies)
      {
        var fileName = $"{file}.dll";
        var backupFileName = $"{file}.dll.bak";
        var newPath = Path.Combine(extractionPath, fileName);
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
      Directory.Delete(extractionPath);
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

    public class GithubRelease
    {
      [JsonProperty("tag_name")]
      public string TagName;
      [JsonProperty("html_url")]
      public string HtmlUrl;
      [JsonProperty("assets")]
      public List<GithubReleaseAsset> Assets;
    }

    public class GithubReleaseAsset
    {
      [JsonProperty("name")]
      public string Name;
      [JsonProperty("browser_download_url")]
      public string BrowserDownloadUrl;
    }
  }
}
