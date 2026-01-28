
using System;
using System.Diagnostics;
using UnityEngine;

namespace StationeersLaunchPad
{
  public static class ProcessUtil
  {
    public static void OpenExplorerDir(string dirPath)
    {
      try
      {
        Process.Start("explorer.exe", $"\"{dirPath}\"");
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
      }
    }

    public static void OpenExplorerSelectFile(string filePath)
    {
      try
      {
        Process.Start("explorer", $"/select,\"{filePath}\"");
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
      }
    }

    public static void RestartGame()
    {

      var startInfo = new ProcessStartInfo
      {
        FileName = LaunchPadPaths.ExecutablePath,
        WorkingDirectory = LaunchPadPaths.GameRootPath,
        UseShellExecute = false
      };

      // remove environment variables that new process will inherit
      startInfo.Environment.Remove("DOORSTOP_INITIALIZED");
      startInfo.Environment.Remove("DOORSTOP_DISABLE");

      Process.Start(startInfo);
      Application.Quit();
    }
  }
}