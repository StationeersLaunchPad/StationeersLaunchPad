using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using StationeersLaunchPad;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Repos;
using UnityEngine;

namespace StationeersLaunchPad.News;

public static class NewsRunner
{
  /// <summary>
  /// Fetches the remote news feed (if configured) and returns the notices that match
  /// the currently enabled mods (after dismissals and "already migrated" checks).
  /// This is the main entrypoint for the news system to obtain relevant notices.
  /// </summary>
  public static async UniTask<List<NewsEntry>> GetActiveNotices(ModList currentModList)
  {
    var url = Configs.EffectiveNewsFeedUrl;
    if (string.IsNullOrWhiteSpace(url)) return [];

    var feed = await NewsFetcher.Fetch(url);
    if (feed == null)
    {
      Logger.Global.LogDebug("News: no feed or fetch failed");
      return [];
    }

    Logger.Global.LogDebug($"News: feed has {feed.Entries?.Count ?? 0} entries");

    var dismissed = NewsDismissal.LoadDismissed();
    var matches = await NewsMatcher.Match(feed, currentModList, dismissed);
    return matches;
  }

  public static async UniTask ExecutePrimaryAction(NewsEntry entry)
  {
    var act = entry?.Actions?.Primary;
    if (act == null)
      return;

    if (act.Action == "open_url")
    {
      if (!string.IsNullOrEmpty(act.Url))
        Application.OpenURL(act.Url);
      return;
    }

    if (act.Action == "repo_mod_install")
    {
      await ExecuteRepoModInstall(act.Url, act.ModId);
    }

    if (act.Action == "workshop_mod_install")
    {
      await ExecuteWorkshopModInstall(act.WorkshopId);
    }
  }

  public static async UniTask ExecuteSecondaryAction(NewsEntry entry)
  {
    var act = entry?.Actions?.Secondary;
    if (act?.Action == "open_url" && !string.IsNullOrEmpty(act.Url))
      Application.OpenURL(act.Url);
  }

  public static async UniTask<bool> ExecuteRepoModInstall(string url, string modId = null)
  {
    if (string.IsNullOrWhiteSpace(url))
      return false;

    try
    {
      var config = ModRepos.Current;
      if (config == null)
      {
        config = await ModRepos.LoadConfig();
        ModRepos.Current = config;
      }

      var httpRepo = HttpRepoDef.FromURL(url, null);
      if (httpRepo == null)
      {
        Logger.Global.LogError($"news: cannot parse repo url '{url}'");
        return false;
      }

      ModRepoDef targetRepo;
      if (!config.Repos.Any(r => r.ID == httpRepo.ID))
      {
        ModRepos.AssignNewRepoDir(config, httpRepo);
        await ModRepos.UpdateRepoData(httpRepo);
        if (httpRepo.Data == null)
        {
          Logger.Global.LogError($"news: failed to fetch or parse modrepo from {url}");
          return false;
        }
        config.Repos.Add(httpRepo);
        targetRepo = httpRepo;
      }
      else
      {
        targetRepo = config.Repos.First(r => r.ID == httpRepo.ID);
        if (targetRepo.Data == null)
          await ModRepos.UpdateRepoData(targetRepo, true);
      }

      var data = targetRepo.Data;
      if (data == null || data.ModVersions.Count == 0)
      {
        Logger.Global.LogError($"news: modrepo at {url} has no versions");
        return false;
      }

      // Use explicit modid from the news action if provided, otherwise fall back to first entry
      string modID = !string.IsNullOrEmpty(modId)
        ? modId
        : data.ModVersions[0].ModID;

      var index = ModRepoIndex.Build(config);
      var target = index.GetLatest(modID, targetRepo.ID, "", null, null)
                   ?? data.ModVersions.FirstOrDefault(v => v.ModID == modID)
                   ?? data.ModVersions[0];

      var def = new RepoModDef
      {
        ModID = modID,
        Branch = "",
        RepoID = targetRepo.ID,
        Repo = targetRepo,
      };

      bool ok = await ModRepos.UpdateMod(config, def);
      if (!ok)
      {
        Logger.Global.LogError($"news: UpdateMod failed for {modID}");
        return false;
      }

      if (!config.Mods.Any(m => m.ModID == modID && m.RepoID == targetRepo.ID))
        config.Mods.Add(def);

      ModRepos.SaveConfig(config);

      Logger.Global.LogInfo($"news: repo mod migration installed {modID} from {targetRepo.ID}");
      return true;
    }
    catch (Exception ex)
    {
      Logger.Global.LogException(ex);
      Logger.Global.LogError("news: repo_mod_install failed");
      return false;
    }
  }

  public static async UniTask<bool> ExecuteWorkshopModInstall(string workshopIdStr, ulong? unsubscribeWorkshopId = null)
  {
    if (!ulong.TryParse(workshopIdStr, out var wid) || wid < 2)
    {
      Logger.Global.LogError($"news: invalid or missing workshop_id '{workshopIdStr}'");
      return false;
    }

    return await Steam.SubscribeAndDownload(wid, unsubscribeWorkshopId);
  }
}
