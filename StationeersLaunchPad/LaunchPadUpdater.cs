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

    private static DirectoryInfo _cachedInstallDir;
    private static DirectoryInfo InstallDir
    {
      get
      {
        if (_cachedInstallDir == null)
        {
          var dir = Directory.GetParent(typeof(LaunchPadUpdater).Assembly.Location);
          if (dir == null || !dir.Exists)
            return null;

          var pluginDir = new DirectoryInfo(Paths.PluginPath);
          var parent = dir;
          var nested = false;
          // ensure install path is inside bepinex plugins
          while (parent != null)
          {
            if (parent.FullName == pluginDir.FullName)
            {
              nested = true;
              break;
            }
            parent = parent.Parent;
          }
          if (!nested)
            return null;
          _cachedInstallDir = dir;
        }
        return _cachedInstallDir;
      }
    }
    private static string TargetAssetName(Github.Release release) => $"StationeersLaunchPad-{(GameManager.IsBatchMode ? "server" : "client")}-{release.TagName}.zip";

    public static void RunPostUpdateCleanup()
    {
      try
      {
        var installDir = InstallDir;
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
        var installDir = InstallDir;
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
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
        Logger.Global.LogError("An error occurred during LaunchPadBooster install. Some mods may not function properly");
        LaunchPadConfig.AutoLoad = false;
      }
    }

    public static async UniTask CheckVersion()
    {
      if (InstallDir == null)
      {
        Logger.Global.LogWarning("Invalid install dir. Skipping update check");
        return;
      }

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

          var path = Path.Combine(InstallDir.FullName, $"{entry.Name}");
          UpdateAction action = File.Exists(path) ? new ReplaceFileUpdateAction(path) : new NewFileUpdateAction(path);
          UpdateActions.Add(action);

          action.PerformUpdate(entry);
        }
      }

      LaunchPadConfig.LoadState = LoadState.Updated;
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
