
using Cysharp.Threading.Tasks;
using Steamworks;
using Steamworks.Ugc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StationeersLaunchPad
{
  public static class Steam
  {
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
  }
}