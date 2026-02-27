
using Assets.Scripts;
using StationeersLaunchPad.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Util.Commands;

namespace StationeersLaunchPad.Commands
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
          var res = instance.ExecuteStartup(cmd);
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

    public override string HelpText => RootCommand.Instance.UsageDescription;

    public override string[] Arguments => new string[] { RootCommand.Instance.ChildNames };

    public override bool IsLaunchCmd => true;

    public override string Execute(string[] args) => Execute(args, false);
    public string ExecuteStartup(string[] args) => Execute(args, true);

    private string Execute(string[] args, bool startup)
    {
      if (!StartupRun)
      {
        StartupCommands.Add(args);
        return null;
      }
      return (startup ? RootCommand.StartupInstance : RootCommand.Instance).Execute(args);
    }
  }

  public abstract class SubCommand
  {
    protected static void Print(string message)
    {
      if (LaunchPadConfig.GameRunning)
        Compat.ConsoleWindowPrint(message);
      Logger.Global.Log(message);
    }

    protected static ArgParser ArgP(ReadOnlySpan<string> args) => new(args);

    public readonly string Name;
    public readonly List<SubCommand> Children;
    public readonly Dictionary<string, SubCommand> ChildrenMap;
    public readonly string ShortUsage;
    public readonly string LongUsage;
    public SubCommand(string name, params SubCommand[] children)
    {
      Name = name.ToLower();
      Children = new(children);
      ChildrenMap = new();
      foreach (var child in children)
        ChildrenMap.Add(child.Name, child);

      var sb = new StringBuilder(Name);
      if (Children.Count > 0)
      {
        sb.Append(" (");
        sb.Append(string.Join(" | ", children.Select(c => c.Name)));
        sb.Append(')');
      }
      sb.Append(' ').Append(UsageDescription);
      ShortUsage = sb.ToString().Trim();

      sb.AppendLine();
      foreach (var child in Children)
      {
        sb.Append("\t");
        if (Name != "")
          sb.Append(Name).Append(' ');
        sb.AppendLine(child.ShortUsage);
      }
      LongUsage = sb.ToString().Trim();
    }

    public abstract string UsageDescription { get; }

    public string Execute(ReadOnlySpan<string> args)
    {
      if (RunChild(args, out var result))
        return result;
      if (RunLeaf(args, out result))
        return result;
      return LongUsage;
    }

    protected bool RunChild(ReadOnlySpan<string> args, out string result)
    {
      if (args.Length == 0)
      {
        result = null;
        return false;
      }
      if (!ChildrenMap.TryGetValue(args[0].ToLower(), out var child))
      {
        result = null;
        return false;
      }
      result = child.Execute(args[1..]);
      return true;
    }

    protected virtual bool RunLeaf(ReadOnlySpan<string> args, out string result)
    {
      result = null;
      return false;
    }
  }

  public ref struct ArgParser
  {
    private readonly ReadOnlySpan<string> args;
    private ulong used;
    private bool valid;

    public ArgParser(ReadOnlySpan<string> args)
    {
      this.args = args;
      used = 0;
      valid = true;
    }

    public ArgParser Positional(out string argVal, string defaultVal)
    {
      for (var i = 0; i < args.Length; i++)
      {
        var flag = 1ul << i;
        if ((used & flag) != 0)
          continue;
        argVal = args[i];
        used |= flag;
        return this;
      }
      argVal = defaultVal;
      return this;
    }

    public ArgParser Positional(out string argVal)
    {
      for (var i = 0; i < args.Length; i++)
      {
        var flag = 1ul << i;
        if ((used & flag) != 0)
          continue;
        argVal = args[i];
        used |= flag;
        return this;
      }
      argVal = null;
      valid = false;
      return this;
    }

    public ArgParser Named(string name, out string argVal, string defaultVal = null)
    {
      if (!valid)
      {
        argVal = defaultVal;
        return this;
      }
      for (var i = 0; i < args.Length; i++)
      {
        var flag = 1ul << i;
        if ((used & flag) != 0)
          continue;
        if (!SplitNamedArg(args[i], out var aname, out var aval))
          continue;
        if (!aname.Equals(name, StringComparison.OrdinalIgnoreCase))
          continue;
        argVal = new(aval);
        used |= flag;
        return this;
      }
      argVal = defaultVal;
      return this;
    }

    private static bool SplitNamedArg(
      string arg, out ReadOnlySpan<char> name, out ReadOnlySpan<char> value)
    {
      var split = arg.IndexOf('=');
      if (split == -1)
      {
        name = value = default;
        return false;
      }
      name = arg.AsSpan(0, split);
      value = arg.AsSpan(split + 1);
      return true;
    }

    public ArgParser Flag(string name, out bool argVal)
    {
      if (!valid)
      {
        argVal = false;
        return this;
      }
      for (var i = 0; i < args.Length; i++)
      {
        var flag = 1ul << i;
        if ((used & flag) != 0)
          continue;
        var arg = args[i];
        if (arg.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
          argVal = true;
          used |= flag;
          return this;
        }
        if (!SplitNamedArg(arg, out var aname, out var aval))
          continue;
        if (!aname.Equals(name, StringComparison.OrdinalIgnoreCase))
          continue;
        argVal = aval == "1" || aname.Equals("true", StringComparison.OrdinalIgnoreCase);
        used |= flag;
        return this;
      }
      argVal = false;
      return this;
    }

    public bool Validate() => valid && (1ul << args.Length) - 1 == used;
  }

  public class RootCommand : SubCommand
  {
    private static readonly SubCommand[] StartupCommands = new SubCommand[]
    {
      new ConfigCommand(),
      new ReposCommand(),
      new RepoModsCommand(),
      new ModPkgCommand(),
      new ExitCommand(),
    };
    private static readonly SubCommand[] InGameCommands = new SubCommand[]
    {
      new LogsCommand(),
    };

    public static readonly RootCommand StartupInstance = new(StartupCommands);
    public static readonly RootCommand Instance = new(StartupCommands, InGameCommands);

    public readonly string ChildNames;

    private RootCommand(SubCommand[] children, params SubCommand[] extraChildren)
    : base("", children.Concat(extraChildren).ToArray()) =>
      ChildNames = string.Join(" | ", Children.Select(c => c.Name));

    public override string UsageDescription => "StationeersLaunchPad Commands";
  }

  public class LogsCommand : SubCommand
  {
    public LogsCommand() : base("logs") { }
    public override string UsageDescription => "-- open mods log window";

    protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
    {
      LogPanel.OpenStandaloneLogs();
      ConsoleWindow.Hide();
      result = null;
      return true;
    }
  }

  public class ModPkgCommand : SubCommand
  {
    public ModPkgCommand() : base("modpkg") { }
    public override string UsageDescription =>
      "[<path.zip>] -- generate mod package for dedicated server or debugging";

    protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
    {
      if (!ArgP(args).Positional(out var pkgpath, null).Validate())
      {
        result = null;
        return false;
      }
      result = LaunchPadConfig.ExportModPackage(pkgpath);
      return true;
    }
  }

  public class ExitCommand : SubCommand
  {
    public ExitCommand() : base("exit") { }
    public override string UsageDescription => "-- exit the game";

    protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
    {
      Application.Quit();
      result = null;
      return true;
    }
  }
}