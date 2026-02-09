
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Repos;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StationeersLaunchPad.Commands
{
  public class RepoModsCommand : SubCommand
  {
    public RepoModsCommand() : base("repomods",
      new ListCommand(),
      new AddCommand(),
      new RemoveCommand())
    { }
    public override string UsageDescription => "-- manage Repo Mods";

    private static bool ParseAddRemove(ReadOnlySpan<string> args,
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
        var ps = arg.Split('=', 2);
        switch (ps[0].ToLower())
        {
          case "branch": branch = ps[1]; break;
          case "version": version = ps[1]; break;
          case "repo": repo = ps[1]; break;
          default:
            return false;
        }
      }
      return true;
    }

    public class ListCommand : SubCommand
    {
      public ListCommand() : base("list") { }
      public override string UsageDescription => "-- list installed repo mods";

      protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
      {
        var sb = new StringBuilder();
        sb.AppendLine($"{ModRepos.Current.Mods.Count} mods");
        foreach (var mod in ModRepos.Current.Mods)
          sb.AppendLine($"{mod.ModID}@{mod.Branch}[{mod.Version}] from {mod.RepoID}");
        result = sb.ToString().TrimEnd();
        return true;
      }
    }

    public class AddCommand : SubCommand
    {
      public AddCommand() : base("add") { }
      public override string UsageDescription =>
        "<ModID> [version=<Version>] [branch=<Branch>] [repo=<RepoID>]";

      protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
      {
        if (!ParseAddRemove(
              args, out var modID, out var branch, out var version, out var repo))
        {
          result = null;
          return false;
        }

        branch ??= "";

        var index = ModRepoIndex.Build(ModRepos.Current);
        if (repo == null)
        {
          var matching = new List<string>();
          foreach (var (k, _) in index.ModRepos(modID))
            matching.Add(k.RepoID);
          if (matching.Count == 0)
          {
            result = $"No repos containing {modID}";
            return true;
          }
          if (matching.Count > 1)
          {
            result = $"Multiple repos containing {modID}:\n" +
              string.Join("\n", matching);
            return true;
          }
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
        {
          result = $"Could not find {modID}@{branch}[{version}] in {repo}";
          return true;
        }

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

        result = null;
        return true;
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
    }

    public class RemoveCommand : SubCommand
    {
      public RemoveCommand() : base("remove") { }
      public override string UsageDescription =>
        "<ModID> [version=<Version>] [branch=<Branch>] [repo=<RepoID>]";

      protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
      {
        if (!ParseAddRemove(
              args, out var modID, out var branch, out var version, out var repo))
        {
          result = null;
          return false;
        }

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
        {
          result = "No matching repo mods";
          return true;
        }

        if (matchIdxs.Count > 1)
        {
          var sb = new StringBuilder();
          sb.AppendLine("Multiple matching mods found");
          foreach (var idx in matchIdxs)
          {
            var mod = config.Mods[idx];
            sb.AppendLine($"{mod.ModID}@{mod.Branch}[{mod.Version}] from {mod.RepoID}");
          }
          result = sb.ToString().Trim();
          return true;
        }

        var match = config.Mods[matchIdxs[0]];
        if (LaunchPadConfig.ModsLoaded && !string.IsNullOrEmpty(match.DirName))
        {
          var dir = Path.Join(LaunchPadPaths.RepoModsPath, match.DirName);
          if (LaunchPadConfig.MatchMod(new LocalModData(dir, false)) != null)
          {
            result = $"Cannot remove loaded mod {match.ModID}@{match.Branch}[{match.Version}]";
            return true;
          }
        }
        config.Mods.RemoveAt(matchIdxs[0]);
        ModRepos.SaveConfig(config);
        ModRepos.CleanRepoModDirs(config);
        LaunchPadConfig.ReloadMods();
        result = $"Removed {match.ModID}@{match.Branch}[{match.Version}] from {match.RepoID}";
        return true;
      }
    }
  }
}