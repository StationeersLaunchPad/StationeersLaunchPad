using Cysharp.Threading.Tasks;
using StationeersLaunchPad.UI;
using System;
using System.IO;

namespace StationeersLaunchPad.Update
{
  public static class LaunchPadUpdater
  {
    private static string TargetAssetName(Github.Release release) =>
      $"StationeersLaunchPad-{(Platform.IsServer ? "server" : "client")}-{release.TagName}.zip";

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
          if (!File.Exists(file.FullName[..^4]))
            continue;
          Logger.Global.LogDebug($"Removing update backup file {file.FullName}");
          file.Delete();
        }
      }
      catch (Exception ex)
      {
        Logger.Global.LogWarning($"error occurred during post update cleanup: {ex.Message}");
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

      if (Version.Compare(latestRelease.TagName, LaunchPadInfo.VERSION) <= 0)
      {
        Logger.Global.LogInfo($"StationeersLaunchPad is up-to-date.");
        return null;
      }

      Logger.Global.LogWarning($"StationeersLaunchPad has an update available.");
      return latestRelease;
    }

    public static async UniTask<bool> CheckShouldUpdate(Github.Release release)
    {
      // if autoupdate is not enabled on server, just move on after the out-of-date message
      if (Platform.IsServer)
        return false;

      return await AlertPopup.ShouldUpdateDialog(
        release.TagName,
        release.FormatDescription(),
        release.HtmlUrl
      );
    }

    // returns true if update was successfully performed
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
