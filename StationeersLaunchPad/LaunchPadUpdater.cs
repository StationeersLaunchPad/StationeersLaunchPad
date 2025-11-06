using Assets.Scripts;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;

namespace StationeersLaunchPad
{
  public static class LaunchPadUpdater
  {
    private static string TargetAssetName(Github.Release release) => $"StationeersLaunchPad-{(GameManager.IsBatchMode ? "server" : "client")}-{release.TagName}.zip";

    public static void RunPostUpdateCleanup()
    {
      try
      {
        var installDir = LaunchPadPaths.InstallDir;
        if (installDir == null)
        {
          Logger.Global.LogWarning("Invalid install dir. skipping post update cleanup");
          return;
        }
        Logger.Global.LogDebug("Running post-update cleanup");
        foreach (var file in installDir.EnumerateFiles("*.dll.bak"))
        {
          // if the matching dll doesn't exist, this probably wasn't from us?
          if (!File.Exists(file.FullName.Substring(0, file.FullName.Length - 4)))
            continue;
          Logger.Global.LogDebug($"Removing update backup file {file.FullName}");
          file.Delete();
        }
        LaunchPadConfig.PostUpdateCleanup.Value = false;
      }
      catch (Exception ex)
      {
        Logger.Global.LogWarning($"error occurred during post update cleanup: {ex.Message}");
      }
    }

    public static async UniTask RunOneTimeBoosterInstall()
    {
      try
      {
        var installDir = LaunchPadPaths.InstallDir;
        if (installDir == null)
        {
          Logger.Global.LogWarning("Invalid install dir. skipping booster install");
          return;
        }
        const string boosterName = "LaunchPadBooster.dll";
        var boosterPath = Path.Combine(installDir.FullName, boosterName);
        if (File.Exists(boosterPath))
        {
          // if file exists, this is a full install so nothing to do
          LaunchPadConfig.OneTimeBoosterInstall.Value = false;
          return;
        }
        var targetTag = $"v{LaunchPadPlugin.pluginVersion}";
        Logger.Global.Log($"Installing LaunchPadBooster from release {targetTag}");
        var release = await Github.LaunchPadRepo.FetchTagRelease(targetTag);
        if (release == null)
        {
          LaunchPadConfig.AutoLoad = false;
          Logger.Global.LogError("Installation incomplete. Please download latest version from github.");
          return;
        }

        var assetName = TargetAssetName(release);
        var asset = release.Assets.Find(asset => asset.Name == assetName);
        if (asset == null)
        {
          LaunchPadConfig.AutoLoad = false;
          Logger.Global.LogError($"Failed to find {assetName} in release. Installation incomplete. Please download latest version from github.");
          return;
        }

        using (var archive = await asset.FetchToMemory())
        {
          var entry = archive.Entries.First(entry => entry.Name == boosterName);
          if (entry == null)
          {
            Logger.Global.LogError($"Failed to find {boosterName} in {assetName}. Installation incomplete. Please download latest version from github.");
            LaunchPadConfig.AutoLoad = false;
            return;
          }
          entry.ExtractToFile(boosterPath);
        }

        LaunchPadConfig.OneTimeBoosterInstall.Value = false;
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
        Logger.Global.LogError("An error occurred during LaunchPadBooster install. Some mods may not function properly");
        LaunchPadConfig.AutoLoad = false;
      }
    }

    public static async UniTask<Github.Release> GetUpdateRelease()
    {
      if (LaunchPadPaths.InstallDir == null)
      {
        Logger.Global.LogWarning("Invalid install dir. Skipping update check");
        return null;
      }

      var latestRelease = await Github.LaunchPadRepo.FetchLatestRelease();
      // If we failed to get a release for whatever reason, just bail
      if (latestRelease == null)
        return null;

      var latestVersion = new Version(latestRelease.TagName.TrimStart('V', 'v'));
      var currentVersion = new Version(LaunchPadPlugin.pluginVersion.TrimStart('V', 'v'));

      if (latestVersion <= currentVersion)
      {
        Logger.Global.LogInfo($"StationeersLaunchPad is up-to-date.");
        return null;
      }

      Logger.Global.LogWarning($"StationeersLaunchPad has an update available.");
      return latestRelease;
    }

    public static async UniTask<bool> CheckShouldUpdate(Github.Release release)
    {
      if (LaunchPadConfig.AutoUpdate)
      {
        return true;
      }
      // if autoupdate is not enabled on server, just move on after the out-of-date message
      if (GameManager.IsBatchMode)
        return false;

      var doUpdate = false;
      bool acceptUpdate()
      {
        doUpdate = true;
        return true;
      }
      bool openGithub()
      {
        Application.OpenURL(release.HtmlUrl);
        return false;
      }
      bool rejectUpdate()
      {
        doUpdate = false;
        return true;
      }

      var description = release.FormatDescription();
      await LaunchPadAlertGUI.Show("Update Available", $"StationeersLaunchPad {release.TagName} is available, would you like to automatically download and update?\n\n{description}",
        new Vector2(800, 400),
        LaunchPadAlertGUI.DefaultPosition,
        ("Yes", acceptUpdate),
        ("View release info", openGithub),
        ("No", rejectUpdate)
      );
      return doUpdate;
    }

    public static async UniTask<bool> UpdateToRelease(Github.Release release)
    {
      var assetName = TargetAssetName(release);
      var asset = release.Assets.Find(a => a.Name == assetName);
      if (asset == null)
      {
        Logger.Global.LogError($"Failed to find {assetName} in release. Skipping update");
        return false;
      }

      using (var archive = await asset.FetchToMemory())
      {
        var sequence = UpdateSequence.Make(
          LaunchPadPaths.InstallDir,
          archive,
          filter: e => e.Name.EndsWith(".dll"),
          mapPath: e => e.Name
        );

        var result = sequence.Execute();
        switch (result)
        {
          case UpdateResult.Success:
            return true;
          case UpdateResult.Rollback:
            Logger.Global.LogError("Update failed. Changes were rolled back");
            return false;
          case UpdateResult.FailedRollback:
            Logger.Global.LogError("Update failed. Rolling back update failed. StationeersLaunchPad may be in an invalid state.");
            return false;
          default:
            throw new InvalidOperationException($"Invalid update result {result}");
        }
      }
    }
  }
}
