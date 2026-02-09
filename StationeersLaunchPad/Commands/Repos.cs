
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
      new IndexCommand())
    { }
    public override string UsageDescription => "-- manage Mod Repos";

    public class ListCommand : SubCommand
    {
      public ListCommand() : base("list") { }
      public override string UsageDescription => "[<searchtext>] -- list connected repos";

      protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
      {
        var repos = ModRepos.Current?.Repos ?? new();
        if (args.Length > 0)
        {
          var matches = new List<ModRepoDef>();
          foreach (var repo in ModRepos.Current?.Repos ?? new())
          {
            if (repo.ID.Contains(args[0], StringComparison.OrdinalIgnoreCase))
              matches.Add(repo);
          }
          repos = matches;
        }
        var sb = new StringBuilder();
        sb.AppendLine($"{repos.Count} repos");
        foreach (var repo in repos)
          sb.AppendLine($"{repo.ID}: {repo.Data?.ModVersions.Count ?? 0} mod versions");
        result = sb.ToString().TrimEnd();
        return true;
      }
    }

    public class AddCommand : SubCommand
    {
      public AddCommand() : base("add") { }
      public override string UsageDescription =>
        "<RepoID> [novalidate] -- connect to a repo";

      protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
      {
        if (args.Length < 1)
        {
          result = null;
          return false;
        }
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
      public override string UsageDescription => "<RepoID> -- remove a connected repo";

      protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
      {
        if (args.Length == 0)
        {
          result = null;
          return false;
        }
        var repoID = args[0];

        var config = ModRepos.Current;
        var idx = config.Repos.FindIndex(r => r.ID == repoID);
        if (idx == -1)
        {
          result = $"No repo with ID {repoID}";
          return true;
        }

        config.Repos.RemoveAt(idx);
        ModRepos.SaveConfig(config);
        result = $"Removed repo {repoID}";
        return true;
      }
    }

    public class IndexCommand : SubCommand
    {
      public IndexCommand() : base("index") { }
      public override string UsageDescription =>
        "[mod=<ModID>] [repo=<RepoID>] [branch=<Branch>] [version/minversion/maxversion=<Version>] -- search connected mod repos";

      protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
      {
        string mod = null;
        string repo = null;
        string branch = null;
        string vmin = null;
        string vmax = null;

        foreach (var arg in args)
        {
          var ps = arg.Split('=', 2);
          if (ps.Length != 2)
          {
            result = null;
            return false;
          }
          switch (ps[0].ToLower())
          {
            case "mod": mod = ps[1]; break;
            case "repo": repo = ps[1]; break;
            case "branch": branch = ps[1]; break;
            case "version": vmin = vmax = ps[1]; break;
            case "minversion": vmin = ps[1]; break;
            case "maxversion": vmax = ps[1]; break;
          }
        }

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