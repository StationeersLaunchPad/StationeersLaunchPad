
using Assets.Scripts;
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Util.Commands;

namespace StationeersLaunchPad.Commands
{
  public enum CommandStage
  {
    PreInit, // before SLP is initialized (shouldn't run any commands yet)
    Init, // wait for SLP initialization
    ConfigLoaded, // wait for mod config to be loaded (mod/repo list)
    ModsLoaded, // wait for mods to be loaded
    GameRunning, // wait for game to be running
  };

  public class SLPCommand : CommandBase
  {
    public static readonly SLPCommand Instance = new();

    private static bool inCommandExec = false;
    private static bool inCommandAsync = false;

    public static CommandStage Stage { get; private set; } = CommandStage.PreInit;
    public static bool CommandRunning => inCommandExec || inCommandAsync;
    private static readonly Queue<string[]> CommandQueue = new();

    public static CommandStage QueuedStage
    {
      get
      {
        if (CommandQueue.Count == 0)
          return CommandStage.Init;
        return CurrentRoot.Stage(CommandQueue.Peek());
      }
    }

    public static string RunCommand(string[] args)
    {
      if (CommandRunning || CommandQueue.Count > 0 || CurrentRoot.Stage(args) > Stage)
      {
        CommandQueue.Enqueue(args);
        return null;
      }
      return DoExecute(args);
    }

    public static async UniTask AsyncCommand(UniTask cmd)
    {
      try
      {
        inCommandAsync = true;
        await cmd;
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
      }
      finally
      {
        inCommandAsync = false;
      }
      TryRunNext();
    }

    private static void TryRunNext()
    {
      while (!CommandRunning && CommandQueue.Count > 0 && QueuedStage <= Stage)
      {
        var res = DoExecute(CommandQueue.Dequeue());
        if (res != null)
          SubCommand.Print(res);
      }
    }

    // move the stage backwards and don't try to immediately run commands
    public static void RevertStage(CommandStage stage)
    {
      if (stage > Stage)
        throw new InvalidOperationException($"{stage} > {Stage}");
      Stage = stage;
    }

    public static async UniTask MoveToStage(CommandStage stage)
    {
      Stage = stage;
      TryRunNext();
      while (CommandRunning)
        await UniTask.Yield();
    }

    public override string HelpText => RootCommand.Instance.UsageDescription;

    public override string[] Arguments => new string[] { RootCommand.Instance.ChildNames };

    public override bool IsLaunchCmd => true;

    public override string Execute(string[] args) => RunCommand(args);

    private static string DoExecute(string[] args)
    {
      inCommandExec = true;
      try
      {
        return CurrentRoot.Execute(args);
      }
      finally
      {
        inCommandExec = false;
      }
    }

    private static RootCommand CurrentRoot =>
      Stage < CommandStage.GameRunning ? RootCommand.StartupInstance : RootCommand.Instance;
  }

  public abstract class SubCommand
  {
    public static void Print(string message)
    {
      if (SLPCommand.Stage >= CommandStage.GameRunning)
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

    public CommandStage Stage(ReadOnlySpan<string> args)
    {
      if (args.Length == 0 || !ChildrenMap.TryGetValue(args[0].ToLower(), out var child))
        return LeafStage;
      return child.Stage(args[1..]);
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

    protected virtual CommandStage LeafStage => CommandStage.Init;
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

    protected override CommandStage LeafStage => CommandStage.Init;
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

    protected override CommandStage LeafStage => CommandStage.ConfigLoaded;
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

    protected override CommandStage LeafStage => CommandStage.Init;
    protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
    {
      Application.Quit();
      result = null;
      return true;
    }
  }
}