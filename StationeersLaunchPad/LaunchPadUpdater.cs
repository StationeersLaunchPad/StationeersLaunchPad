using Assets.Scripts;
using BepInEx;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;

namespace StationeersLaunchPad
{
  // similar to jixxed's updater
  public static class LaunchPadUpdater
  {
    public static bool AllowUpdate;

    private static List<UpdateAction> UpdateActions = new();

    private static string PluginPath => Path.Combine(Paths.PluginPath, "StationeersLaunchPad");
    private static string TargetAssetName(Github.Release release) => GameManager.IsBatchMode ? $"StationeersLaunchPad-server-{release.TagName}.zip" : $"StationeersLaunchPad-{release.TagName}.zip";

    public static void RunPostUpdateCleanup()
    {
      Logger.Global.LogDebug("Running post-update cleanup");
      foreach (var file in Directory.EnumerateFiles(PluginPath, "*.dll.bak"))
      {
        Logger.Global.LogDebug($"Removing update backup file {file}");
        File.Delete(Path.Combine(PluginPath, file));
      }
      LaunchPadConfig.PostUpdateCleanup.Value = false;
    }

    public static async UniTask RunOneTimeBoosterInstall()
    {
      const string boosterName = "LaunchPadBooster.dll";
      var boosterPath = Path.Combine(PluginPath, boosterName);
      if (File.Exists(boosterPath))
      {
        // if file exists, this is a full install so nothing to do
        LaunchPadConfig.OneTimeBoosterInstall.Value = false;
        return;
      }
      var targetTag = $"v{LaunchPadPlugin.pluginVersion}";
      Logger.Global.Log($"Installing LaunchPadBooster from release {targetTag}");
      var release = await Github.FetchTagRelease(targetTag);
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

      using (var archive = await Github.FetchZipArchive(asset))
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

    public static async UniTask CheckVersion()
    {
      var latestRelease = await Github.FetchLatestRelease();
      // If we failed to get a release for whatever reason, just bail
      if (latestRelease == null)
        return;

      var latestVersion = new Version(latestRelease.TagName.TrimStart('V', 'v'));
      var currentVersion = new Version(LaunchPadPlugin.pluginVersion.TrimStart('V', 'v'));

      if (latestVersion <= currentVersion)
      {
        Logger.Global.LogInfo($"Plugin is up-to-date.");
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
        LaunchPadConfig.PostUpdateCleanup.Value = true;
        Logger.Global.LogError($"Mod loader has been updated to version {latestVersion}, please restart your game!");
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
        Logger.Global.LogError("An error occurred during update. Rolling back");
        RevertUpdate();
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

    private static async UniTask PerformUpdate(Github.Release release)
    {
      var assetName = TargetAssetName(release);
      var asset = release.Assets.Find(a => a.Name == assetName);
      if (asset == null)
      {
        Logger.Global.LogError($"Failed to find {assetName} in release. Skipping update");
        return;
      }

      using (var archive = await Github.FetchZipArchive(asset))
      {
        foreach (var entry in archive.Entries)
        {
          if (!entry.Name.EndsWith(".dll"))
            continue;

          var path = Path.Combine(PluginPath, $"{entry.Name}");
          UpdateAction action = File.Exists(path) ? new ReplaceFileUpdateAction(path) : new NewFileUpdateAction(path);
          UpdateActions.Add(action);

          action.PerformUpdate(entry);
        }
      }
    }

    private static void RevertUpdate()
    {
      try
      {
        foreach (var action in UpdateActions)
        {
          action.RevertUpdate();
        }

        Logger.Global.LogWarning($"Mod loader has reverted update changes due to an error.");
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
        Logger.Global.LogError("Error rolling back update. StationeersLaunchPad install may be in an invalid state.");
      }
    }

    private abstract class UpdateAction
    {
      public abstract void PerformUpdate(ZipArchiveEntry entry);
      public abstract void RevertUpdate();
    }

    private class NewFileUpdateAction : UpdateAction
    {
      private string path;
      public NewFileUpdateAction(string path) => this.path = path;

      public override void PerformUpdate(ZipArchiveEntry entry)
      {
        Logger.Global.LogDebug($"Extracting new file to {this.path}");
        entry.ExtractToFile(this.path);
      }

      public override void RevertUpdate()
      {
        if (File.Exists(this.path))
        {
          Logger.Global.LogDebug($"Removing new file {this.path}");
          File.Delete(this.path);
        }
      }
    }

    private class ReplaceFileUpdateAction : UpdateAction
    {
      private string path;
      public ReplaceFileUpdateAction(string path) => this.path = path;

      private string backupPath => $"{this.path}.bak";

      public override void PerformUpdate(ZipArchiveEntry entry)
      {
        Logger.Global.LogDebug($"Replacing file {this.path}");

        if (File.Exists(this.backupPath))
        {
          Logger.Global.LogDebug($"Removing old backup {this.backupPath}");
          File.Delete(this.backupPath);
        }

        Logger.Global.LogDebug($"Backing up existing file to {this.backupPath}");
        File.Move(this.path, this.backupPath);

        Logger.Global.LogDebug($"Extracting new file to {this.path}");
        entry.ExtractToFile(this.path);
      }

      public override void RevertUpdate()
      {
        if (!File.Exists(this.backupPath))
        {
          // nothing to do
          return;
        }

        if (File.Exists(this.path))
        {
          Logger.Global.LogDebug($"Removing new file {this.path}");
          File.Delete(this.path);
        }

        Logger.Global.LogDebug($"Restoring backup file {this.backupPath}");
        File.Move(this.backupPath, this.path);
      }
    }
  }
}
