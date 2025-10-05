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
    public static bool AllowUpdate;

    private static readonly List<UpdateAction> UpdateActions = [];
    private static readonly Lazy<DirectoryInfo> _installDir = new(() =>
    {
      var dir = Directory.GetParent(typeof(LaunchPadUpdater).Assembly.Location);
      if (dir == null || !dir.Exists)
        return null;

      var pluginDir = new DirectoryInfo(LaunchPadPaths.PluginPath);
      while (dir != null)
      {
        if (dir.FullName == pluginDir.FullName)
          return dir;
        dir = dir.Parent;
      }

      return null;
    });

    private static DirectoryInfo InstallDir => _installDir.Value;

    private static string TargetAssetName(Github.Release release) =>
        $"StationeersLaunchPad-{(GameManager.IsBatchMode ? "server" : "client")}-{release.TagName}.zip";

    public static void RunPostUpdateCleanup()
    {
      if (InstallDir == null)
      {
        Logger.Global.LogWarning("Invalid install dir. skipping post update cleanup");
        return;
      }

      Logger.Global.LogDebug("Running post-update cleanup");
      foreach (var file in InstallDir.EnumerateFiles("*.dll.bak"))
      {
        var originalPath = Path.ChangeExtension(file.FullName, null);
        if (!File.Exists(originalPath))
          continue;

        Logger.Global.LogDebug($"Removing update backup file {file.FullName}");
        file.Delete();
      }

      LaunchPadConfig.PostUpdateCleanup.Value = false;
    }

    public static async UniTask RunOneTimeBoosterInstall()
    {
      if (InstallDir == null)
      {
        Logger.Global.LogWarning("Invalid install dir. skipping booster install");
        return;
      }

      const string boosterName = "LaunchPadBooster.dll";
      var boosterPath = Path.Combine(InstallDir.FullName, boosterName);
      if (File.Exists(boosterPath))
      {
        LaunchPadConfig.OneTimeBoosterInstall.Value = false;
        return;
      }

      var targetTag = $"v{LaunchPadPlugin.pluginVersion}";
      Logger.Global.Log($"Installing LaunchPadBooster from release {targetTag}");

      var release = await Github.FetchTagRelease(targetTag);
      if (release == null)
      {
        LaunchPadConfig.AutoLoad = false;
        Logger.Global.LogError("Installation incomplete. Please download latest version from GitHub.");
        return;
      }

      var asset = FindAsset(release, TargetAssetName(release));
      if (asset == null)
      {
        LaunchPadConfig.AutoLoad = false;
        return;
      }

      await ExtractEntryFromZip(asset, boosterName, boosterPath);

      LaunchPadConfig.OneTimeBoosterInstall.Value = false;
    }

    public static async UniTask CheckVersion()
    {
      if (InstallDir == null)
      {
        Logger.Global.LogWarning("Invalid install dir. Skipping update check");
        return;
      }

      var latestRelease = await Github.FetchLatestRelease();
      if (latestRelease == null)
        return;

      var latestVersion = new Version(latestRelease.TagName.TrimStart('v', 'V'));
      var currentVersion = new Version(LaunchPadPlugin.pluginVersion.TrimStart('v', 'V'));

      if (latestVersion <= currentVersion)
      {
        Logger.Global.LogInfo("Plugin is up-to-date.");
        return;
      }

      Logger.Global.LogWarning("Plugin is NOT up-to-date.");

      await CheckShouldUpdate(latestRelease);

      if (!AllowUpdate)
        return;

      try
      {
        await PerformUpdate(latestRelease);
        LaunchPadConfig.HasUpdated = true;
        LaunchPadConfig.PostUpdateCleanup.Value = true;
        Logger.Global.LogError($"Mod loader updated to version {latestVersion}. Please restart the game!");
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
        Logger.Global.LogError("Error during update. Rolling back...");
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

      if (GameManager.IsBatchMode)
        return;

      var description = Github.FormatDescription(release.Description);
      await LaunchPadAlertGUI.Show("Update Available",
          $"Update version {release.TagName} is available. Would you like to automatically download and update?\n\n{description}",
          new Vector2(800, 400),
          LaunchPadAlertGUI.DefaultPosition,
          ("Yes", () => { AllowUpdate = true; return true; }
      ),
          ("Open GitHub", () => { AllowUpdate = false; Application.OpenURL(release.HtmlUrl); return false; }
      ),
          ("No", () => { AllowUpdate = false; return true; }
      )
      );
    }

    private static async UniTask PerformUpdate(Github.Release release)
    {
      var assetName = TargetAssetName(release);
      var asset = FindAsset(release, assetName);
      if (asset == null)
        return;

      using var archive = await Github.FetchZipArchive(asset);
      foreach (var entry in archive.Entries.Where(e => e.Name.EndsWith(".dll")))
      {
        var path = Path.Combine(InstallDir.FullName, entry.Name);
        UpdateAction action = File.Exists(path) ? new ReplaceFileUpdateAction(path) : new NewFileUpdateAction(path);
        UpdateActions.Add(action);
        action.PerformUpdate(entry);
      }

      LaunchPadConfig.LoadState = LoadState.Updated;
    }

    private static void RevertUpdate()
    {
      try
      {
        foreach (var action in UpdateActions)
          action.RevertUpdate();

        Logger.Global.LogWarning("Mod loader reverted update due to an error.");
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
        Logger.Global.LogError("Rollback failed. LaunchPad install may be in an invalid state.");
      }
    }

    private static Github.Asset FindAsset(Github.Release release, string assetName) =>
        release.Assets.FirstOrDefault(a =>
        {
          if (a.Name == assetName)
            return true;
          Logger.Global.LogError($"Failed to find {assetName} in release {release.TagName}");
          return false;
        });

    private static async UniTask ExtractEntryFromZip(Github.Asset asset, string entryName, string targetPath)
    {
      using var archive = await Github.FetchZipArchive(asset);
      var entry = archive.Entries.FirstOrDefault(e => e.Name == entryName);
      if (entry == null)
      {
        Logger.Global.LogError($"Failed to find {entryName} in {asset.Name}");
        LaunchPadConfig.AutoLoad = false;
        return;
      }

      Logger.Global.LogDebug($"Extracting {entryName} to {targetPath}");
      entry.ExtractToFile(targetPath);
    }

    private abstract class UpdateAction
    {
      public abstract void PerformUpdate(ZipArchiveEntry entry);
      public abstract void RevertUpdate();
    }

    private class NewFileUpdateAction : UpdateAction
    {
      private readonly string _path;
      public NewFileUpdateAction(string path) => this._path = path;

      public override void PerformUpdate(ZipArchiveEntry entry)
      {
        Logger.Global.LogDebug($"Extracting new file {this._path}");
        entry.ExtractToFile(this._path);
      }

      public override void RevertUpdate()
      {
        if (File.Exists(this._path))
        {
          Logger.Global.LogDebug($"Removing new file {this._path}");
          File.Delete(this._path);
        }
      }
    }

    private class ReplaceFileUpdateAction : UpdateAction
    {
      private readonly string _path;
      public ReplaceFileUpdateAction(string path) => this._path = path;
      private string BackupPath => $"{this._path}.bak";

      public override void PerformUpdate(ZipArchiveEntry entry)
      {
        Logger.Global.LogDebug($"Replacing file {this._path}");

        if (File.Exists(this.BackupPath))
        {
          Logger.Global.LogDebug($"Removing old backup {this.BackupPath}");
          File.Delete(this.BackupPath);
        }

        Logger.Global.LogDebug($"Backing up existing file to {this.BackupPath}");
        File.Move(this._path, this.BackupPath);

        Logger.Global.LogDebug($"Extracting new file to {this._path}");
        entry.ExtractToFile(this._path);
      }

      public override void RevertUpdate()
      {
        if (!File.Exists(this.BackupPath))
          return;

        if (File.Exists(this._path))
        {
          Logger.Global.LogDebug($"Removing new file {this._path}");
          File.Delete(this._path);
        }

        Logger.Global.LogDebug($"Restoring backup file {this.BackupPath}");
        File.Move(this.BackupPath, this._path);
      }
    }
  }
}
