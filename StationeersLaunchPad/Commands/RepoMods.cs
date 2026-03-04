
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Repos;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StationeersLaunchPad.Commands
{
  public class RepoModsCommand : SubCommand
  {
    public RepoModsCommand() : base("repomods",
      new ListCommand(),
      new AddCommand(),
      new RemoveCommand(),
      new UpdateCommand(),
      new UpdateAllCommand())
    { }
    public override string UsageDescription => "-- manage Repo Mods";

    public class ListCommand : SubCommand
    {
      public ListCommand() : base("list") { }
      public override string UsageDescription => "-- list installed repo mods";

      protected override CommandStage LeafStage => CommandStage.ConfigLoaded;
      protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
      {
        if (!ArgP(args).Validate())
        {
          result = null;
          return false;
        }
        var sb = new StringBuilder();
        var mods = ModRepos.Current.Mods;
        sb.AppendLine($"{mods.Count} mods");
        for (var i = 0; i < mods.Count; i++)
        {
          var mod = mods[i];
          sb.AppendLine($"[{i}] {mod.ModID}@{mod.Branch}[{mod.Version}] from {mod.RepoID}");
        }
        result = sb.ToString().TrimEnd();
        return true;
      }
    }

    public class AddCommand : SubCommand
    {
      public AddCommand() : base("add") { }
      public override string UsageDescription =>
        "<ModID> [version/minversion/maxversion=<Version>] [branch=<Branch>] [repo=<RepoID>]";

      protected override CommandStage LeafStage => CommandStage.ConfigLoaded;
      protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
      {
        if (!ArgP(args).Named("version", out var version)
                        .Named("minversion", out var minv)
                        .Named("maxversion", out var maxv)
                        .Named("branch", out var branch)
                        .Named("repo", out var repo)
                        .Positional(out var modID)
                        .Validate())
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
        minv ??= version;

        ModVersionData target = null;
        foreach (var (k, v) in index.Versions(modID, repo, branch))
        {
          if (minv is not null && Version.Compare(k.Version, minv) < 0)
            continue;
          if (maxv is not null && Version.Compare(k.Version, maxv) > 0)
            continue;
          target = v;
        }

        if (target == null)
        {
          result = $"No available mod versions matching {modID}@{branch}[{minv},{maxv}] in {repo}";
          return true;
        }

        var mod = new RepoModDef
        {
          ModID = modID,
          Branch = branch,
          RepoID = repo,
          MinVersion = minv ?? target.Version,
          MaxVersion = maxv,
          Repo = ModRepos.Current.Repos.First(r => r.ID == repo),
        };
        if (version != null)
          mod.MaxVersion = version;

        SLPCommand.AsyncCommand(Add(mod)).Forget();

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

    private static bool ParseSelectArgs(
      ReadOnlySpan<string> args,
      out string modID, out string version, out string branch, out string repo
    ) =>
      ArgP(args).Named("version", out version)
                .Named("branch", out branch)
                .Named("repo", out repo)
                .Positional(out modID)
                .Validate();

    private static int SelectMod(ModReposConfig config,
      string modID, string version, string branch, string repo, out string error)
    {
      if (!int.TryParse(modID, out var index) || index >= config.Mods.Count)
        index = -1;

      if (index < 0)
      {
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
          error = "No matching repo mods";
          return -1;
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
          error = sb.ToString().Trim();
          return -1;
        }
        index = matchIdxs[0];
      }
      error = null;
      return index;
    }

    public class RemoveCommand : SubCommand
    {
      public RemoveCommand() : base("remove") { }
      public override string UsageDescription =>
        "<ModID|Index> [version=<Version>] [branch=<Branch>] [repo=<RepoID>]";

      protected override CommandStage LeafStage => CommandStage.ConfigLoaded;
      protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
      {
        if (!ParseSelectArgs(args, out var modID, out var version, out var branch, out var repo))
        {
          result = null;
          return false;
        }

        var config = ModRepos.Current;
        var index = SelectMod(config, modID, version, branch, repo, out result);
        if (index < 0)
          return true;

        var match = config.Mods[index];
        if (LaunchPadConfig.ModsLoaded && !string.IsNullOrEmpty(match.DirName))
        {
          var dir = Path.Join(LaunchPadPaths.RepoModsPath, match.DirName);
          if (LaunchPadConfig.MatchMod(new LocalModData(dir, false)) != null)
          {
            result = $"Cannot remove loaded mod {match.ModID}@{match.Branch}[{match.Version}]";
            return true;
          }
        }
        config.Mods.RemoveAt(index);
        ModRepos.SaveConfig(config);
        ModRepos.CleanRepoModDirs(config);
        LaunchPadConfig.ReloadMods();
        result = $"Removed {match.ModID}@{match.Branch}[{match.Version}] from {match.RepoID}";
        return true;
      }
    }

    public class UpdateCommand : SubCommand
    {
      public UpdateCommand() : base("update") { }
      public override string UsageDescription =>
        "<ModID|Index> [version=<Version>] [branch=<Branch>] [repo=<RepoID>]";

      protected override CommandStage LeafStage => CommandStage.ConfigLoaded;
      protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
      {
        if (!ParseSelectArgs(args, out var modID, out var version, out var branch, out var repo))
        {
          result = null;
          return false;
        }

        var config = ModRepos.Current;
        var index = SelectMod(config, modID, version, branch, repo, out result);
        if (index < 0)
          return true;

        var match = config.Mods[index];
        if (LaunchPadConfig.ModsLoaded && !string.IsNullOrEmpty(match.DirName))
        {
          var dir = Path.Join(LaunchPadPaths.RepoModsPath, match.DirName);
          if (LaunchPadConfig.MatchMod(new LocalModData(dir, false)) != null)
          {
            result = $"Cannot update loaded mod {match.ModID}@{match.Branch}[{match.Version}]";
            return true;
          }
        }

        if (!ModRepos.TryPickModUpdate(config, match, out var update))
        {
          result = "No update available";
          return true;
        }

        SLPCommand.AsyncCommand(UpdateMod(update)).Forget();

        result = null;
        return true;
      }

      private static async UniTask UpdateMod(RepoModUpdateTarget update)
      {
        var config = ModRepos.Current;
        await ModRepos.PerformModUpdate(update);
        ModRepos.SaveConfig(config);
        LaunchPadConfig.ReloadMods();
      }
    }

    public class UpdateAllCommand : SubCommand
    {
      public UpdateAllCommand() : base("updateall") { }
      public override string UsageDescription => "-- update all installed repo mods";

      protected override CommandStage LeafStage => CommandStage.ConfigLoaded;
      protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
      {
        if (!ArgP(args).Validate())
        {
          result = null;
          return false;
        }

        var config = ModRepos.Current;
        var targets = ModRepos.GetModUpdateTargets(config);

        if (targets.Count == 0)
        {
          result = "No updates available";
          return true;
        }

        SLPCommand.AsyncCommand(UpdateAll(targets)).Forget();

        result = null;
        return true;
      }

      private static async UniTask UpdateAll(List<RepoModUpdateTarget> targets)
      {
        var config = ModRepos.Current;
        await ModRepos.UpdateMods(config, targets);
        ModRepos.SaveConfig(config);
        LaunchPadConfig.ReloadMods();
      }
    }
  }
}