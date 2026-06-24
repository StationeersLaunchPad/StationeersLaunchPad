using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Repos;
using UnityEngine;

namespace StationeersLaunchPad.News;

public static class NewsRunner
{
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

      LaunchPadConfig.InvalidateCachedSearchData();

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
}
