
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace StationeersLaunchPad.Update;

public enum UpdateResult
{
  None, Success, Rollback, FailedRollback
}

public class UpdateSequence
{
  public static UpdateSequence Make(
    DirectoryInfo installDir,
    ZipArchive archive,
    Func<ZipArchiveEntry, bool> filter = null,
    Func<ZipArchiveEntry, string> mapPath = null)
  {
    var seq = new UpdateSequence();
    foreach (var entry in archive.Entries)
    {
      if (filter != null && !filter(entry))
        continue;

      var path = Path.Combine(installDir.FullName, mapPath?.Invoke(entry) ?? entry.FullName);
      var exists = File.Exists(path);
      seq.Actions.Add(
        exists
        ? new ReplaceFileFromZipAction(path, entry)
        : new NewFileFromZipAction(path, entry));
    }
    return seq;
  }

  public readonly List<UpdateAction> Actions = [];

  public UpdateResult Execute()
  {
    try
    {
      foreach (var action in Actions)
        action.PerformUpdate();
    }
    catch (Exception ex)
    {
      Logger.Global.LogException(ex);
      return Rollback();
    }

    try
    {
      foreach (var action in Actions)
        action.FinishUpdate();
    }
    catch (Exception ex)
    {
      // log uncaught exceptions, but don't fail the update
      Logger.Global.LogException(ex);
    }

    return UpdateResult.Success;
  }

  private UpdateResult Rollback()
  {
    try
    {
      for (var i = Actions.Count; --i >= 0;)
      {
        Actions[i].RevertUpdate();
      }
      return UpdateResult.Rollback;
    }
    catch (Exception ex)
    {
      Logger.Global.LogException(ex);
      return UpdateResult.FailedRollback;
    }
  }
}

public abstract class UpdateAction
{
  // perform primary update step. if any of these fail, the update will rollback
  public abstract void PerformUpdate();
  // perform final cleanup steps if update succeeded. these will not cause a rollback on failure
  public abstract void FinishUpdate();
  // undo any actions performed to restore the pre-update state
  public abstract void RevertUpdate();
}

public class NewFileFromZipAction(string path, ZipArchiveEntry entry) : UpdateAction
{
  private readonly string path = path;
  private readonly ZipArchiveEntry entry = entry;

  public override void PerformUpdate()
  {
    Logger.Global.LogDebug($"Extracting new file to {path}");
    entry.ExtractToFile(path);
  }

  public override void FinishUpdate()
  {
  }

  public override void RevertUpdate()
  {
    if (File.Exists(path))
    {
      Logger.Global.LogDebug($"Removing new file {path}");
      File.Delete(path);
    }
  }
}

public class ReplaceFileFromZipAction(string path, ZipArchiveEntry entry) : UpdateAction
{
  private readonly string path = path;
  private readonly ZipArchiveEntry entry = entry;

  private string BackupPath => $"{path}.bak";

  public override void PerformUpdate()
  {
    Logger.Global.LogDebug($"Replacing file {path}");

    if (File.Exists(BackupPath))
    {
      Logger.Global.LogDebug($"Removing old backup {BackupPath}");
      File.Delete(BackupPath);
    }

    Logger.Global.LogDebug($"Backing up existing file to {BackupPath}");
    File.Move(path, BackupPath);

    Logger.Global.LogDebug($"Extracting new file to {path}");
    entry.ExtractToFile(path);
  }

  public override void FinishUpdate()
  {
    try
    {
      File.Delete(BackupPath);
    }
    catch
    {
      // ignore failures deleting the backup files
    }
  }

  public override void RevertUpdate()
  {
    if (!File.Exists(BackupPath))
    {
      // nothing to do
      return;
    }

    if (File.Exists(path))
    {
      Logger.Global.LogDebug($"Removing new file {path}");
      File.Delete(path);
    }

    Logger.Global.LogDebug($"Restoring backup file {BackupPath}");
    File.Move(BackupPath, path);
  }
}