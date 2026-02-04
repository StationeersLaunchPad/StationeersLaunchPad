
using Assets.Scripts;
using StationeersLaunchPad.UI;
using System;
using System.Collections.Generic;
using UnityEngine;
using Util.Commands;

namespace StationeersLaunchPad
{
  public class SLPCommand : CommandBase
  {
    public static bool StartupRun = false;
    public static readonly List<string[]> StartupCommands = new();

    public static void RunStartup()
    {
      StartupRun = true;
      var instance = new SLPCommand();
      foreach (var cmd in StartupCommands)
      {
        try
        {
          var res = instance.Execute(cmd);
          if (!string.IsNullOrEmpty(res))
            Compat.ConsoleWindowPrint($"slp: {res}");
        }
        catch (Exception ex)
        {
          ConsoleWindow.PrintError($"Exception: {ex}", true);
        }
      }
      StartupCommands.Clear();
    }

    public override string HelpText => string.Join('\n',
      "StationeersLaunchPad Commands",
      "\tmodpkg -- generate mod package for dedicated server or debugging",
      "\tlogs -- open mod logs window",
      "\tquit -- exits after StationeersLaunchPad finishes loading and runs prior commands"
    );

    public override string[] Arguments => new string[] { "modpkg | logs | quit" };

    public override bool IsLaunchCmd => true;

    public override string Execute(string[] args)
    {
      if (args.Length == 0)
        return HelpText;
      if (!StartupRun)
      {
        StartupCommands.Add(args);
        return null;
      }
      return args[0].ToLowerInvariant() switch
      {
        "modpkg" => ModPkg(),
        "logs" => Logs(),
        "quit" => Quit(),
        _ => HelpText,
      };
    }

    private string ModPkg() => LaunchPadConfig.ExportModPackage();

    private string Logs()
    {
      LogPanel.OpenStandaloneLogs();
      ConsoleWindow.Hide();
      return null;
    }

    private string Quit()
    {
      Application.Quit();
      return null;
    }
  }
}