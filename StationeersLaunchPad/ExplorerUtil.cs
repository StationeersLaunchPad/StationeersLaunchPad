
using System;
using System.Diagnostics;

namespace StationeersLaunchPad
{
  public static class ExplorerUtil
  {
    public static void Open(string dirPath)
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

    public static void OpenDirectorySelect(string filePath)
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
  }
}