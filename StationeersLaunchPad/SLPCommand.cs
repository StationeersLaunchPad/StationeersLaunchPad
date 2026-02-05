
using Assets.Scripts;
using BepInEx.Configuration;
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Repos;
using StationeersLaunchPad.UI;
using System;
using System.Collections.Generic;
using System.IO;
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
      new SLPCmdRepos(),
      new SLPCmdRepoMods(),
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

    protected static void Print(string message)
    {
      if (LaunchPadConfig.GameRunning)
        Compat.ConsoleWindowPrint(message);
      Logger.Global.Log(message);
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
          sb.AppendLine($"{def.Section}/{def.Key}={cfg.GetSerializedValue()}");
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
            matches.Add(cfg);
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

  public class SLPCmdRepos : SLPCmd
  {
    public override string Name => "repos";
    public override string Description => "manage Mod Repos";
    public override string ExtendedDescription => string.Join("\n",
      "repos list [<repo>]",
      "repos add <repo> [novalidate]",
      "repos remove <repo>"
    );

    public override string Execute(Span<string> args)
    {
      if (args.Length == 0)
        return ExtendedDescription;
      return args[0].ToLower() switch
      {
        "list" => List(args[1..]),
        "add" => Add(args[1..]),
        "remove" => Remove(args[1..]),
        "index" => Index(args[1..]),
        _ => ExtendedDescription,
      };
    }

    private string List(Span<string> args)
    {
      var config = ModRepos.Current;
      if (config == null) return null;
      var sb = new StringBuilder();
      if (args.Length == 0)
      {
        sb.AppendLine($"{config.Repos.Count} repos");
        foreach (var repo in config.Repos)
          sb.AppendLine($"{repo.ID}: {repo.Data?.ModVersions.Count ?? 0} mod versions");
        return sb.ToString().TrimEnd();
      }
      var repoID = args[0];
      var match = config.Repos.FirstOrDefault(r => r.ID == repoID);
      if (match == null)
        return $"No repo with ID {repoID}";

      if (match.Data == null)
        return $"No data for {repoID}";
      sb.AppendLine($"{match.Data.ModVersions.Count} mod versions");
      foreach (var mod in match.Data.ModVersions)
      {
        var branches = string.Join(",", mod.Branches.Select(b => (string) b));
        sb.AppendLine($"{mod.ModID}@{branches}[{mod.Version}]");
      }

      return sb.ToString().Trim();
    }

    private string Add(Span<string> args)
    {
      if (args.Length < 1)
        return ExtendedDescription;
      var repoUrl = args[0];
      var validate = args.Length < 2 || args[1].ToLower() != "novalidate";

      var httpUrl = repoUrl;
      if (httpUrl.ToLower().StartsWith("http://"))
        httpUrl = $"https://{httpUrl[7..]}";
      if (!httpUrl.ToLower().StartsWith("https://"))
        httpUrl = $"https://{httpUrl}";

      var match = Github.RepoRegex.Match(repoUrl);
      ModRepoDef repo = match.Success ? new GitHubRepoDef()
      {
        Owner = match.Groups[1].Value,
        Name = match.Groups[2].Value
      } : new HttpRepoDef() { Url = httpUrl };
      Add(repo, validate).Forget();
      return null;
    }

    private static async UniTask Add(ModRepoDef repo, bool validate)
    {
      var config = ModRepos.Current;
      if (config.Repos.Any(r => r.ID == repo.ID))
      {
        Print($"already added repo {repo.ID}");
        return;
      }
      ModRepos.AssignNewRepoDir(config, repo);
      await ModRepos.UpdateRepoData(repo);
      if (repo.Data == null && validate)
      {
        Print($"error loading {repo.ID}. skipping add");
        return;
      }
      config.Repos.Add(repo);
      ModRepos.SaveConfig(config);
      Print($"Added repo {repo.ID}");
    }

    private string Remove(Span<string> args)
    {
      if (args.Length == 0)
        return ExtendedDescription;
      var repoID = args[0];

      var config = ModRepos.Current;
      var idx = config.Repos.FindIndex(r => r.ID == repoID);
      if (idx == -1)
        return $"No repo with ID {repoID}";

      config.Repos.RemoveAt(idx);
      ModRepos.SaveConfig(config);
      return $"Removed repo {repoID}";
    }

    private string Index(Span<string> args)
    {
      var index = ModRepoIndex.Build(ModRepos.Current);
      var sb = new StringBuilder();
      foreach (var (k, _) in index)
      {
        sb.AppendLine($"{k.ModID}@{k.Branch}[{k.Version}] in {k.RepoID}");
      }
      return sb.ToString().Trim();
    }
  }

  public class SLPCmdRepoMods : SLPCmd
  {
    public override string Name => "repomods";
    public override string Description => "manage Repo Mods";
    public override string ExtendedDescription => string.Join("\n",
      "repomods list",
      "repomods add <ModID> [version=<Version>] [branch=<Branch>] [repo=<RepoID>]",
      "repomods remove <ModID> [version=<Version>] [branch=<Branch>] [repo=<RepoID>]"
    );

    public override string Execute(Span<string> args)
    {
      if (args.Length == 0)
        return ExtendedDescription;
      return args[0].ToLower() switch
      {
        "list" => List(args[1..]),
        "add" => Add(args[1..]),
        "remove" => Remove(args[1..]),
        _ => ExtendedDescription,
      };
    }

    private string List(Span<string> args)
    {
      var sb = new StringBuilder();
      sb.AppendLine($"{ModRepos.Current.Mods.Count} mods");
      foreach (var mod in ModRepos.Current.Mods)
        sb.AppendLine($"{mod.ModID}@{mod.Branch}[{mod.Version}] from {mod.RepoID}");
      return sb.ToString().TrimEnd();
    }
    private string Add(Span<string> args)
    {
      if (!ParseAddRemove(args, out var modID, out var branch, out var version, out var repo))
        return ExtendedDescription;

      branch ??= "";

      var index = ModRepoIndex.Build(ModRepos.Current);
      if (repo == null)
      {
        var matching = new List<string>();
        foreach (var (k, _) in index.ModRepos(modID))
          matching.Add(k.RepoID);
        if (matching.Count == 0)
          return $"No repos containing {modID}";
        if (matching.Count > 1)
          return $"Multiple repos containing {modID}:\n" + string.Join("\n", matching);
        repo = matching[0];
      }

      ModVersionData target = null;
      foreach (var (k, v) in index.Versions(modID, repo, branch))
      {
        if (version == null)
          target = v;
        else if (version == k.Version)
          target = v;
      }

      if (target == null)
        return $"Could not find {modID}@{branch}[{version}] in {repo}";

      var mod = new RepoModDef
      {
        ModID = modID,
        Branch = branch,
        RepoID = repo,
        MinVersion = target.Version,
      };
      if (version != null)
        mod.MaxVersion = version;

      Add(mod).Forget();

      return null;
    }

    private static async UniTask Add(RepoModDef mod)
    {
      var config = ModRepos.Current;
      if (!await ModRepos.UpdateMod(config, mod))
      {
        Print($"Could not install {mod.ModID}. skipping add");
        return;
      }
      config.Mods.Add(mod);
      ModRepos.SaveConfig(config);
      LaunchPadConfig.ReloadMods();
    }

    private string Remove(Span<string> args)
    {
      if (!ParseAddRemove(args, out var modID, out var branch, out var version, out var repo))
        return ExtendedDescription;

      var config = ModRepos.Current;

      var matchIdxs = new List<int>();
      for (var i = 0; i < config.Mods.Count; i++)
      {
        var mod = config.Mods[i];
        if (mod.ModID != modID) continue;
        if (branch != null && branch != mod.Branch) continue;
        if (version != null && version != mod.Version) continue;
        if (repo != null && repo != mod.RepoID) continue;
        matchIdxs.Add(i);
      }

      if (matchIdxs.Count == 0)
        return "No matching repo mods";

      if (matchIdxs.Count > 1)
      {
        var sb = new StringBuilder();
        sb.AppendLine("Multiple matching mods found");
        foreach (var idx in matchIdxs)
        {
          var mod = config.Mods[idx];
          sb.AppendLine($"{mod.ModID}@{mod.Branch}[{mod.Version}] from {mod.RepoID}");
        }
        return sb.ToString().Trim();
      }

      var match = config.Mods[matchIdxs[0]];
      if (LaunchPadConfig.ModsLoaded && !string.IsNullOrEmpty(match.DirName))
      {
        var dir = Path.Join(LaunchPadPaths.RepoModsPath, match.DirName);
        if (LaunchPadConfig.MatchMod(new LocalModData(dir, false)) != null)
          return $"Cannot remove loaded mod {match.ModID}@{match.Branch}[{match.Version}]";
      }
      config.Mods.RemoveAt(matchIdxs[0]);
      ModRepos.SaveConfig(config);
      ModRepos.CleanRepoModDirs(config);
      LaunchPadConfig.ReloadMods();
      return $"Removed {match.ModID}@{match.Branch}[{match.Version}] from {match.RepoID}";
    }

    private static bool ParseAddRemove(Span<string> args,
      out string modID, out string branch, out string version, out string repo)
    {
      if (args.Length == 0)
      {
        modID = branch = version = repo = null;
        return false;
      }
      modID = args[0];
      branch = version = repo = null;
      foreach (var arg in args[1..])
      {
        var larg = arg.ToLower();
        if (larg.StartsWith("branch="))
          branch = arg[7..];
        else if (larg.StartsWith("version="))
          version = arg[8..];
        else if (larg.StartsWith("repo="))
          repo = arg[5..];
        else
          return false;
      }
      return true;
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