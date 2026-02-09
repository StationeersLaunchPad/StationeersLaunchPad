
using System;
using System.Collections.Generic;

namespace StationeersLaunchPad.Repos
{
  public class ModRepoIndex
  {
    public static ModRepoIndex Build(ModReposConfig config)
    {
      var entries = new List<IndexEntry>();
      static IndexEntry makeEntry(string repoID, ModVersionData modv, string branch) =>
        new()
        {
          ModID = modv.ModID,
          RepoID = repoID,
          Branch = branch,
          Version = modv.Version,
          Mod = modv,
          NextRepo = -1,
          NextMod = -1,
          NextBranch = -1,
        };
      foreach (var repo in config.Repos)
      {
        if (repo.Data == null)
          continue;
        var repoID = repo.ID;
        foreach (var modv in repo.Data.ModVersions)
        {
          // if no branches listed, assume empty branch
          if ((modv.Branches?.Count ?? 0) == 0)
          {
            entries.Add(makeEntry(repoID, modv, ""));
            continue;
          }
          foreach (var branch in modv.Branches)
            entries.Add(makeEntry(repoID, modv, branch));
        }
      }

      if (entries.Count == 0)
        return new(entries);

      entries.Sort();

      var nextMod = entries.Count;
      var nextRepo = entries.Count;
      var nextBranch = entries.Count;
      var last = entries[^1];
      for (var i = entries.Count; --i >= 0;)
      {
        var entry = entries[i];
        if (entry.ModID != last.ModID)
          nextMod = nextRepo = nextBranch = i + 1;
        else if (entry.RepoID != last.RepoID)
          nextRepo = nextBranch = i + 1;
        else if (entry.Branch != last.Branch)
          nextBranch = i + 1;

        entry.NextMod = nextMod;
        entry.NextRepo = nextRepo;
        entry.NextBranch = nextBranch;
        last = entries[i] = entry;
      }
      return new(entries);
    }

    private readonly List<IndexEntry> entries;
    private ModRepoIndex(List<IndexEntry> entries) => this.entries = entries;

    public Iterator GetEnumerator() => new(this, IndexLevel.Version, 0, entries.Count);

    public Iterator ModIDs() => MakeIter(IndexLevel.Mod);
    public Iterator ModRepos(string modID) => MakeIter(IndexLevel.Repo, modID);
    public Iterator Branches(string modID, string repoID) =>
      MakeIter(IndexLevel.Branch, modID, repoID);
    public Iterator Versions(string modID, string repoID, string branch) =>
      MakeIter(IndexLevel.Version, modID, repoID, branch);

    public ModVersionData GetLatest(
      string modID, string repoID, string branch,
      string minVersion, string maxVersion)
    {
      var start = entries.BinarySearch(new()
      {
        ModID = modID,
        RepoID = repoID,
        Branch = branch,
        Version = minVersion
      });
      if (start < 0)
        start = ~start;
      if (start >= entries.Count)
        return null;

      var first = entries[start];
      if (first.ModID != modID || first.RepoID != repoID || first.Branch != branch)
        return null;

      var end = first.NextBranch;
      var latest = end - 1;
      if (!string.IsNullOrEmpty(maxVersion))
      {
        var maxEntry = new IndexEntry()
        {
          ModID = modID,
          RepoID = repoID,
          Branch = branch,
          Version = maxVersion,
        };
        latest = entries.BinarySearch(maxEntry);
        if (latest < 0)
          latest = ~latest;
        if (latest >= end || maxEntry.CompareTo(entries[latest]) > 0)
          latest--;
      }
      if (latest < start || latest >= end)
        return null;
      return entries[latest].Mod;
    }

    private Iterator MakeIter(IndexLevel level, string modID = null,
      string repoID = null, string branch = null)
    {
      if (level == IndexLevel.Mod)
        return new(this, level, 0, entries.Count);
      var start = entries.BinarySearch(
        new() { ModID = modID, RepoID = repoID, Branch = branch });
      if (start < 0)
        start = ~start;
      if (start >= entries.Count)
        return default;
      var entry = entries[start];
      if (entry.ModID != modID)
        return default;
      if (level > IndexLevel.Repo && entry.RepoID != repoID)
        return default;
      if (level > IndexLevel.Branch && entry.Branch != branch)
        return default;
      return new(this, level, start, level switch
      {
        IndexLevel.Repo => entry.NextMod,
        IndexLevel.Branch => entry.NextRepo,
        IndexLevel.Version => entry.NextBranch,
        _ => throw new InvalidOperationException($"{level}"),
      });
    }

    public struct Key
    {
      public string ModID;
      public string RepoID;
      public string Branch;
      public string Version;
    }

    public enum IndexLevel { Mod, Repo, Branch, Version }
    private struct IndexEntry : IEquatable<IndexEntry>, IComparable<IndexEntry>
    {
      // key
      public string ModID;
      public string RepoID;
      public string Branch;
      public string Version;
      public Key Key => new()
      {
        ModID = ModID,
        RepoID = RepoID,
        Branch = Branch,
        Version = Version
      };

      // value
      public ModVersionData Mod;

      // next indices for each level
      public int NextMod;
      public int NextRepo;
      public int NextBranch;

      public int CompareTo(IndexEntry other)
      {
        int cmp;
        if ((cmp = string.Compare(ModID, other.ModID)) != 0) return cmp;
        if ((cmp = string.Compare(RepoID, other.RepoID)) != 0) return cmp;
        if ((cmp = string.Compare(Branch, other.Branch)) != 0) return cmp;
        return StationeersLaunchPad.Version.Compare(Version, other.Version);
      }

      public bool Equals(IndexEntry other) =>
        string.Equals(ModID, other.ModID) &&
        string.Equals(RepoID, other.RepoID) &&
        string.Equals(Branch, other.Branch) &&
        string.Equals(Version, other.Version);

      public override bool Equals(object other) =>
        other is IndexEntry kother && Equals(kother);
      public override int GetHashCode() =>
        HashCode.Combine(ModID, ModID, Branch, Version);
    }

    public struct Iterator
    {
      private readonly List<IndexEntry> entries;
      public readonly IndexLevel Level;
      private readonly int end;
      private int index;
      private bool first;

      public Iterator(ModRepoIndex index, IndexLevel level, int start, int end)
      {
        this.entries = index.entries;
        this.Level = level;
        this.end = end;
        this.index = start;
        first = true;
      }

      public Iterator GetEnumerator() => this;

      public bool MoveNext()
      {
        if (first)
        {
          first = false;
          return index < end;
        }
        if (index >= end)
          return false;
        var entry = entries[index];
        index = Level switch
        {
          IndexLevel.Mod => entry.NextMod,
          IndexLevel.Repo => entry.NextRepo,
          IndexLevel.Branch => entry.NextBranch,
          IndexLevel.Version => index + 1,
          _ => throw new InvalidOperationException($"{Level}"),
        };
        return index < end;
      }

      public (Key Key, ModVersionData Version) Current =>
        new(entries[index].Key, entries[index].Mod);
    }
  }
}