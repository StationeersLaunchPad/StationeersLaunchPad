
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Repos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StationeersLaunchPad.Commands
{
  public class ReposCommand : SubCommand
  {
    public ReposCommand() : base("repos",
      new ListCommand(),
      new AddCommand(),
      new RemoveCommand(),
      new UpdateCommand(),
      new UpdateAllCommand(),
      new IndexCommand())
    { }
    public override string UsageDescription => "-- manage Mod Repos";

    public class ListCommand : SubCommand
    {
      public ListCommand() : base("list") { }
      public override string UsageDescription => "[<searchtext>] -- list connected repos";

      protected override CommandStage LeafStage => CommandStage.ConfigLoaded;
      protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
      {
        if (!ArgP(args).Positional(out var filter, null).Validate())
        {
          result = null;
          return false;
        }
        var matches = new List<(int, ModRepoDef)>();
        var repos = ModRepos.Current?.Repos ?? new();
        for (var i = 0; i < repos.Count; i++)
        {
          var repo = repos[i];
          if (filter is null || repo.ID.Contains(filter, StringComparison.OrdinalIgnoreCase))
            matches.Add((i, repo));
        }
        var sb = new StringBuilder();
        sb.AppendLine($"{matches.Count} repos");
        foreach (var (index, repo) in matches)
        {
          sb.Append($"[{index}] {repo.DisplayName}: ");
          sb.Append($"{repo.Data?.ModVersions.Count ?? 0} mod versions");
          if (repo.DisplayName != repo.ID)
            sb.Append($" ({repo.ID})");
          sb.AppendLine();
        }
        result = sb.ToString().TrimEnd();
        return true;
      }
    }

    public class AddCommand : SubCommand
    {
      public AddCommand() : base("add") { }
      public override string UsageDescription =>
        "<RepoURL> [name=<DisplayName>] [novalidate] -- connect to a repo";

      protected override CommandStage LeafStage => CommandStage.ConfigLoaded;
      protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
      {
        if (!ArgP(args).Flag("novalidate", out var novalidate)
                      .Named("name", out var name, null)
                      .Positional(out var repoUrl)
                      .Validate())
        {
          result = null;
          return false;
        }
        var repo = HttpRepoDef.FromURL(repoUrl, name);
        if (repo == null)
        {
          result = $"invalid repo url '{repoUrl}'";
          return true;
        }

        SLPCommand.AsyncCommand(Add(repo, !novalidate)).Forget();
        result = null;
        return true;
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
    }

    public class RemoveCommand : SubCommand
    {
      public RemoveCommand() : base("remove") { }
      public override string UsageDescription => "<RepoID|Index> -- remove a connected repo";

      protected override CommandStage LeafStage => CommandStage.ConfigLoaded;
      protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
      {
        if (!ArgP(args).Positional(out var repoID).Validate())
        {
          result = null;
          return false;
        }
        var config = ModRepos.Current;
        if (!int.TryParse(repoID, out var index) || index >= config.Repos.Count)
          index = config.Repos.FindIndex(r => r.ID == repoID);

        if (index < 0)
        {
          result = $"No repo with ID {repoID}";
          return true;
        }

        var removed = config.Repos[index];
        config.Repos.RemoveAt(index);
        ModRepos.SaveConfig(config);
        result = $"Removed repo {removed.ID}";
        return true;
      }
    }

    public class UpdateCommand : SubCommand
    {
      public UpdateCommand() : base("update") { }
      public override string UsageDescription => "<RepoID|Index> -- fetch updated repo data";

      protected override CommandStage LeafStage => CommandStage.ConfigLoaded;
      protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
      {
        if (!ArgP(args).Positional(out var repoID).Validate())
        {
          result = null;
          return false;
        }

        var config = ModRepos.Current;
        if (!int.TryParse(repoID, out var index) || index >= config.Repos.Count)
          index = config.Repos.FindIndex(repo => repo.ID == repoID);
        if (index < 0)
        {
          result = $"No repo with ID {repoID}";
          return true;
        }
        SLPCommand.AsyncCommand(UpdateOne(config.Repos[index])).Forget();
        result = null;
        return true;
      }

      private static async UniTask UpdateOne(ModRepoDef repo)
      {
        var config = ModRepos.Current;
        await ModRepos.UpdateRepoData(repo, true);
        ModRepos.SaveConfig(config);
      }
    }

    public class UpdateAllCommand : SubCommand
    {
      public UpdateAllCommand() : base("updateall") { }
      public override string UsageDescription => "-- fetch updated repo data for all connected repos";

      protected override CommandStage LeafStage => CommandStage.ConfigLoaded;
      protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
      {
        if (!ArgP(args).Validate())
        {
          result = null;
          return false;
        }
        SLPCommand.AsyncCommand(UpdateAll()).Forget();
        result = null;
        return true;
      }

      private static async UniTask UpdateAll()
      {
        var config = ModRepos.Current;
        await ModRepos.UpdateRepos(config, true);
        ModRepos.SaveConfig(config);
      }
    }

    public class IndexCommand : SubCommand
    {
      public IndexCommand() : base("index") { }
      public override string UsageDescription =>
        "[mod=<ModID>] [repo=<RepoID>] [branch=<Branch>] [version/minversion/maxversion=<Version>] -- search connected mod repos";

      protected override CommandStage LeafStage => CommandStage.ConfigLoaded;
      protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
      {
        if (!ArgP(args).Named("mod", out var mod)
                      .Named("repo", out var repo)
                      .Named("branch", out var branch)
                      .Named("minversion", out var vmin)
                      .Named("maxversion", out var vmax)
                      .Named("version", out var version)
                      .Validate())
        {
          result = null;
          return false;
        }
        vmin ??= version;
        vmax ??= version;

        if (vmin != null && vmax != null && Version.Compare(vmin, vmax) > 0)
        {
          result = $"minversion '{vmin}' is higher than maxversion '{vmax}'";
          return true;
        }

        var index = ModRepoIndex.Build(ModRepos.Current);
        var matches = new List<ModRepoIndex.Key>();
        foreach (var (k, _) in index)
        {
          if (mod != null && mod != k.ModID)
            continue;
          if (repo != null && repo != k.RepoID)
            continue;
          if (branch != null && branch != k.Branch)
            continue;
          if (vmin != null && Version.Compare(k.Version, vmin) < 0)
            continue;
          if (vmax != null && Version.Compare(k.Version, vmax) > 0)
            continue;
          matches.Add(k);
        }
        var sb = new StringBuilder();
        sb.AppendLine($"{matches.Count} mod versions");
        foreach (var k in matches)
          sb.AppendLine($"{k.ModID}@{k.Branch}[{k.Version}] in {k.RepoID}");
        result = sb.ToString().Trim();
        return true;
      }
    }
  }
}