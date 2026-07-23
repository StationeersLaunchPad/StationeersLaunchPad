using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Repos;
using StationeersLaunchPad.Sources;

namespace StationeersLaunchPad.News;

public static class NewsMatcher
{
  private static readonly XmlSerializer RepoDataSerializer = new(typeof(ModRepoData));

  public static async UniTask<List<NewsEntry>> Match(NewsFeed feed, ModList modList, HashSet<string> dismissed)
  {
    if (feed?.Entries == null || modList == null)
      return [];

    var dismissedSet = dismissed ?? new HashSet<string>(StringComparer.Ordinal);
    var installedRepoModIDs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var installedWorkshopIds = new HashSet<ulong>();
    foreach (var mod in modList.AllMods)
    {
      if (mod.Source == ModSourceType.Repo && !string.IsNullOrEmpty(mod.ModID))
        installedRepoModIDs.Add(mod.ModID);
      if (mod.WorkshopHandle > 1)
        installedWorkshopIds.Add(mod.WorkshopHandle);
    }

    var targetCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    var results = new List<NewsEntry>();

    foreach (var entry in feed.Entries)
    {
      if (entry == null || string.IsNullOrEmpty(entry.Id))
        continue;

      if (dismissedSet.Contains(entry.Id))
      {
        Logger.Global.LogDebug($"News: skipping {entry.Id} (dismissed)");
        continue;
      }

      if (!TriggerMatches(entry.Trigger, modList))
      {
        Logger.Global.LogDebug($"News: skipping {entry.Id} (no matching enabled mod)");
        continue;
      }

      if (ulong.TryParse(entry.Trigger.UnlessWorkshopId, out var excludedWid)
          && excludedWid > 1
          && installedWorkshopIds.Contains(excludedWid))
      {
        Logger.Global.LogDebug($"News: skipping {entry.Id} (excluded workshop mod already installed)");
        continue;
      }

      if (NeedsAlreadyMigratedCheck(entry.Type) && entry.Actions?.Primary != null)
      {
        var primary = entry.Actions.Primary;
        bool hasReplacement = false;
        if (primary.Action == "repo_mod_install")
        {
          if (!string.IsNullOrEmpty(primary.ModId))
          {
            hasReplacement = installedRepoModIDs.Contains(primary.ModId);
          }
          else
          {
            var url = primary.Url ?? "";
            if (!targetCache.TryGetValue(url, out var targets))
            {
              targets = await ResolveTargetModIDs(url);
              targetCache[url] = targets;
            }
            hasReplacement = targets.Any(id => installedRepoModIDs.Contains(id));
          }
        }
        else if (primary.Action == "workshop_mod_install")
        {
          if (ulong.TryParse(primary.WorkshopId, out var targetWid) && targetWid > 1)
          {
            hasReplacement = installedWorkshopIds.Contains(targetWid);
          }
        }
        if (hasReplacement)
        {
          Logger.Global.LogDebug($"News: skipping {entry.Id} (replacement already installed)");
          continue;
        }
      }

      results.Add(entry);
    }

    return results;
  }

  private static bool TriggerMatches(NewsTrigger trigger, ModList modList)
  {
    if (trigger == null)
      return false;

    var matchType = trigger.MatchType ?? "";

    // Unconditional trigger for showcase / testing purposes
    if (matchType == "always")
      return true;
    ulong wid = 0;
    if (!string.IsNullOrEmpty(trigger.WorkshopId))
      ulong.TryParse(trigger.WorkshopId, out wid);
    var versionBelow = trigger.VersionBelow ?? "";

    foreach (var mod in modList.EnabledMods)
    {
      if (mod.Source == ModSourceType.Core)
        continue;

      if (matchType is "workshop_id" or "workshop_id_and_version")
      {
        if (wid > 1 && mod.WorkshopHandle == wid)
        {
          if (matchType == "workshop_id")
            return true;
          var ver = mod.About?.Version ?? "";
          if (string.IsNullOrEmpty(ver) || StationeersLaunchPad.Version.Compare(ver, versionBelow) < 0)
            return true;
        }
      }
      else if (matchType == "mod_name_and_version")
      {
        if (string.Equals(mod.Name, trigger.ModName, StringComparison.OrdinalIgnoreCase))
        {
          var ver = mod.About?.Version ?? "";
          if (string.IsNullOrEmpty(ver) || StationeersLaunchPad.Version.Compare(ver, versionBelow) < 0)
            return true;
        }
      }
    }
    return false;
  }

  private static bool NeedsAlreadyMigratedCheck(string type)
  {
    return type is "migration_needed" or "mod_broken";
  }

  private static async UniTask<HashSet<string>> ResolveTargetModIDs(string url)
  {
    if (string.IsNullOrWhiteSpace(url))
      return [];

    try
    {
      var res = await HttpRepoDef.FetchHttp(url, etag: null);
      if (res.Data == null || res.Data.Length == 0)
        return [];

      using var ms = new MemoryStream(res.Data);
      var data = RepoDataSerializer.Deserialize(ms) as ModRepoData;
      if (data?.ModVersions == null)
        return [];

      var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var mv in data.ModVersions)
        if (!string.IsNullOrEmpty(mv.ModID))
          ids.Add(mv.ModID);
      return ids;
    }
    catch (Exception ex)
    {
      Logger.Global.LogException(ex);
      return [];
    }
  }
}
