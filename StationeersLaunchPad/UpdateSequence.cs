
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace StationeersLaunchPad
{
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

    public readonly List<UpdateAction> Actions = new();

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
        return this.Rollback();
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

  public class NewFileFromZipAction : UpdateAction
  {
    private string path;
    private ZipArchiveEntry entry;
    public NewFileFromZipAction(string path, ZipArchiveEntry entry)
    {
      this.path = path;
      this.entry = entry;
    }

    public override void PerformUpdate()
    {
      Logger.Global.LogDebug($"Extracting new file to {this.path}");
      entry.ExtractToFile(this.path);
    }

    public override void FinishUpdate()
    {
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

  public class ReplaceFileFromZipAction : UpdateAction
  {
    private string path;
    private ZipArchiveEntry entry;
    public ReplaceFileFromZipAction(string path, ZipArchiveEntry entry)
    {
      this.path = path;
      this.entry = entry;
    }

    private string backupPath => $"{this.path}.bak";

    public override void PerformUpdate()
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

    public override void FinishUpdate()
    {
      try
      {
        File.Delete(this.backupPath);
      }
      catch
      {
        // ignore failures deleting the backup files
      }
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