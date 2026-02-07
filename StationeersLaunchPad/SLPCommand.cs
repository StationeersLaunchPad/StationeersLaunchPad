
using Assets.Scripts;
using BepInEx.Configuration;
using StationeersLaunchPad.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Util.Commands;

namespace StationeersLaunchPad
{
  public class SLPCommand : CommandBase
  {
    public static readonly SLPCommand Instance = new();

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

    public override string HelpText => SLPCmd.AllUsage;

    public override string[] Arguments => new string[] { SLPCmd.CmdList };

    public override bool IsLaunchCmd => true;

    public override string Execute(string[] args) => Execute(args, false);
    public string ExecuteStartup(string[] args) => Execute(args, true);

    private string Execute(string[] args, bool startup)
    {
      var usage = startup ? SLPCmd.StartupUsage : SLPCmd.AllUsage;
      if (args.Length == 0) return usage;
      if (!StartupRun)
      {
        StartupCommands.Add(args);
        return null;
      }
      if (!SLPCmd.CommandMap.TryGetValue(args[0].ToLowerInvariant(), out var cmd))
        return usage;
      return cmd.Execute(args.AsSpan(1));
    }
  }

  public abstract class SLPCmd
  {
    public static readonly List<SLPCmd> AllCommands = new()
    {
      new SLPCmdModPkg(),
      new SLPCmdLogs(),
      new SLPCmdConfig(),
      new SLPCmdExit(),
    };
    public static readonly Dictionary<string, SLPCmd> CommandMap = new();
    public static readonly int MaxNameLength;
    public static readonly string AllUsage;
    public static readonly string StartupUsage;
    public static readonly string CmdList;
    static SLPCmd()
    {
      var maxLen = 0;
      foreach (var cmd in AllCommands)
        maxLen = Math.Max(maxLen, cmd.Name.Length);
      MaxNameLength = maxLen;

      var allSb = new StringBuilder("StationeersLaunchPad Commands");
      var startSb = new StringBuilder();
      var listSb = new StringBuilder();

      var first = true;
      foreach (var cmd in AllCommands)
      {
        allSb.AppendLine();
        allSb.Append('\t').Append(cmd.PaddedName);
        allSb.Append(" -- ").Append(cmd.Description);
        if (cmd.IsStartup)
        {
          if (!first) startSb.AppendLine();
          startSb.Append(cmd.StartupDescLine);
        }
        if (!first) listSb.Append(" | ");
        listSb.Append(cmd.Name);
        first = false;

        CommandMap.Add(cmd.Name.ToLowerInvariant(), cmd);
      }
      AllUsage = allSb.ToString();
      StartupUsage = startSb.ToString();
      CmdList = listSb.ToString();
    }

    public abstract string Name { get; }
    public abstract string Description { get; }
    public virtual string StartupDescription => Description;
    public virtual bool IsStartup => true;
    public virtual string ExtendedDescription => null;

    private string paddedName;
    public string PaddedName =>
      paddedName ??= Name + new string(' ', MaxNameLength - Name.Length);

    private string startupDescLine;
    public string StartupDescLine =>
      startupDescLine ??= $"{PaddedName} -- {StartupDescription}";

    public abstract string Execute(Span<string> args);
  }

  public class SLPCmdModPkg : SLPCmd
  {
    public override string Name => "modpkg";
    public override string Description => "generate mod package for dedicated server or debugging";
    public override string Execute(Span<string> args) => LaunchPadConfig.ExportModPackage();
  }

  public class SLPCmdLogs : SLPCmd
  {
    public override string Name => "logs";
    public override string Description => "open mod logs window";
    public override bool IsStartup => false;
    public override string Execute(Span<string> args)
    {
      LogPanel.OpenStandaloneLogs();
      ConsoleWindow.Hide();
      return null;
    }
  }

  public class SLPCmdConfig : SLPCmd
  {
    private const string LIST_USAGE = "config list [<searchtext>]";
    private const string SET_USAGE = "config set <name> <value>";
    public override string Name => "config";
    public override string Description => "list or set a LaunchPad configuration setting";
    public override string ExtendedDescription => string.Join('\n', LIST_USAGE, SET_USAGE);
    public override string Execute(Span<string> args)
    {
      if (args.Length == 0)
        return List("");
      return args[0].ToLowerInvariant() switch
      {
        "list" => List(args.Length > 1 ? args[1] : ""),
        "set" => Set(args[1..]),
        _ => ExtendedDescription,
      };
    }

    private string List(string filter)
    {
      var parts = filter.Split('.', 2);
      var (section, key) = parts.Length == 2 ? (parts[0], parts[1]) : (null, filter);

      var sb = new StringBuilder();
      foreach (var category in Configs.Sorted.Categories)
      {
        if (section != null && !category.Category.Contains(section, StringComparison.OrdinalIgnoreCase))
          continue;
        var fullCat = section == null && category.Category.Contains(key, StringComparison.OrdinalIgnoreCase);
        foreach (var cfg in category.Entries)
        {
          var def = cfg.Definition;
          if (!fullCat && !def.Key.Contains(key, StringComparison.OrdinalIgnoreCase))
            continue;
          sb.AppendLine($"{def.Section}/{def.Key}={cfg.Entry.GetSerializedValue()}");
        }
      }

      return sb.ToString().TrimEnd();
    }

    private string Set(Span<string> args)
    {
      if (args.Length < 2)
        return SET_USAGE;

      var parts = args[0].Split('.', 2);
      var (section, key) = parts.Length == 2 ? (parts[0], parts[1]) : (null, args[0]);

      var matches = new List<ConfigEntryBase>();
      foreach (var category in Configs.Sorted.Categories)
      {
        if (section != null && !section.Equals(category.Category, StringComparison.OrdinalIgnoreCase))
          continue;
        foreach (var cfg in category.Entries)
        {
          if (cfg.Definition.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            matches.Add(cfg.Entry);
        }
      }
      if (matches.Count == 0)
        return $"No configs found for \"{args[0]}\"";

      if (matches.Count > 1)
      {
        var matchstr = string.Join(", ", matches.Select(cfg => $"\"{cfg.Definition}\""));
        return $"\"{args[0]}\" is ambiguous between {matchstr}";
      }

      var prevVal = matches[0].GetSerializedValue();
      matches[0].SetSerializedValue(args[1]);
      var newVal = matches[0].GetSerializedValue();
      return $"Changed \"{matches[0].Definition}\" from \"{prevVal}\" to \"{newVal}\"";
    }
  }

  public class SLPCmdExit : SLPCmd
  {
    public override string Name => "exit";
    public override string Description =>
      "exits after StationeersLaunchPad finishes loading and runs prior commands";
    public override string StartupDescription => "exits the game";
    public override string Execute(Span<string> args)
    {
      Application.Quit();
      return null;
    }
  }
}