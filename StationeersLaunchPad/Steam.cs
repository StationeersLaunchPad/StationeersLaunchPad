
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Sources;
using Steamworks;
using Steamworks.Ugc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace StationeersLaunchPad
{
  public static class Steam
  {
    public const int MOD_NAME_SIZE_LIMIT = 128;
    public const int MOD_DESCRIPTION_SIZE_LIMIT = 8000;
    public const int MOD_CHANGELOG_SIZE_LIMIT = 8000;
    public const int MOD_THUMBNAIL_SIZE_LIMIT = 1024 * 1024;

    public static async UniTask<List<Item>> LoadWorkshopItems()
    {
      var allItems = new List<Item>();
      var page = 1;
      const int batchSize = 5; // number of pages to fetch in parallel

      while (true)
      {
        // Prepare batch of pages
        var pageTasks = new List<UniTask<Item[]>>();
        for (var i = 0; i < batchSize; i++)
        {
          var currentPage = page + i;
          pageTasks.Add(FetchWorkshopPage(currentPage));
        }

        var results = await UniTask.WhenAll(pageTasks);
        var hasItems = false;
        foreach (var items in results)
        {
          if (items.Length > 0)
          {
            allItems.AddRange(items);
            hasItems = true;
          }
        }

        if (!hasItems)
          break; // no more pages

        page += batchSize;
      }

      // Determine which items need updates
      var needsUpdate = allItems.Where(item => item.NeedsUpdate || !Directory.Exists(item.Directory)).ToList();
      if (needsUpdate.Count > 0)
      {
        Logger.Global.Log($"Updating {needsUpdate.Count} workshop items");
        foreach (var item in needsUpdate)
          Logger.Global.LogInfo($"- {item.Title} ({item.Id})");

        await UniTask.WhenAll(needsUpdate.Select(item => item.DownloadAsync().AsUniTask()));
      }

      return allItems;
    }

    // Helper to fetch a single workshop page
    private static async UniTask<Item[]> FetchWorkshopPage(int page)
    {
      var query = Query.Items.WithTag("Mod");
      using var result = await query.AllowCachedResponse(0).WhereUserSubscribed().GetPageAsync(page);

      return !result.HasValue || result.Value.ResultCount == 0
        ? Array.Empty<Item>()
        : result.Value.Entries.Where(item => item.Result != Result.FileNotFound).ToArray();
    }

    public static (bool, string) ValidateForWorkshop(ModInfo mod)
    {
      // if its core its fine, if its on the workshop in the first place its probably fine too
      if (mod.Source != ModSourceType.Local)
        return (true, string.Empty);

      if (mod.About == null)
        return (false, "Mod has invalid/no about data.");

      if (mod.About.Name?.Length > MOD_NAME_SIZE_LIMIT)
        return (false, $"Mod name is larger than {MOD_NAME_SIZE_LIMIT} characters, current size is {mod.About.Name?.Length} characters.");

      if (mod.About.Description?.Length > MOD_DESCRIPTION_SIZE_LIMIT)
        return (false, $"Mod description is larger than {MOD_DESCRIPTION_SIZE_LIMIT} characters, current size is {mod.About.Description?.Length} characters.");

      if (mod.About.ChangeLog?.Length > MOD_CHANGELOG_SIZE_LIMIT)
        return (false, $"Mod changelog is larger than {MOD_CHANGELOG_SIZE_LIMIT} characters, current size is {mod.About.ChangeLog?.Length} characters.");

      if (!File.Exists(mod.ThumbnailPath))
        return (false, $"Mod does not have a thumb.png in the About folder.");

      var thumbnailInfo = new FileInfo(mod.ThumbnailPath);
      if (thumbnailInfo?.Length > MOD_THUMBNAIL_SIZE_LIMIT)
        return (false, $"Mod thumbnail size is larger than {MOD_THUMBNAIL_SIZE_LIMIT / 1024} kilobytes, current size is {thumbnailInfo?.Length / 1024} kilobytes.");

      return (true, string.Empty);
    }

    public static void OpenWorkshopPage(ulong handle)
    {
      try
      {
        Application.OpenURL($"steam://url/CommunityFilePage/{handle}");
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
      }
    }
  }
}