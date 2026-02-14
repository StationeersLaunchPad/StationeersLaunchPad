
using Assets.Scripts.Serialization;
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Metadata;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Xml.Serialization;
using UnityEngine.Networking;

namespace StationeersLaunchPad.Repos
{
  public static class ModRepos
  {
    public static ModReposConfig Current;

    public static void SaveConfig(ModReposConfig config)
    {
      if (!config.SaveXml(LaunchPadPaths.ModReposConfigPath))
        Logger.Global.LogError($"failed to save {LaunchPadPaths.ModReposConfigPath}");
    }

    public static async UniTask<ModReposConfig> LoadConfig()
    {
      await UniTask.SwitchToThreadPool();

      ModReposConfig config;
      if (File.Exists(LaunchPadPaths.ModReposConfigPath))
        config = XmlSerialization.Deserialize<ModReposConfig>(
          LaunchPadPaths.ModReposConfigPath) ?? new();
      else
        config = new();

      var repos = new Dictionary<string, ModRepoDef>();
      var distinctRepos = new List<ModRepoDef>();
      for (var i = 0; i < config.Repos.Count; i++)
      {
        var repo = config.Repos[i];
        if (!repos.ContainsKey(repo.ID))
        {
          repos[repo.ID] = repo;
          distinctRepos.Add(repo);
        }
        else
          Logger.Global.LogDebug($"Removing duplicate mod repo {repo.ID}");
      }
      config.Repos = distinctRepos;

      foreach (var mod in config.Mods)
      {
        if (!repos.TryGetValue(mod.RepoID, out mod.Repo))
          Logger.Global.LogWarning($"No connected repo for {mod.DisplayName}");
      }

      var dirs = new DirAssignment();
      foreach (var repo in config.Repos)
        repo.DirName = dirs.Assign(repo.DirName, repo.ID);

      Logger.Global.LogDebug("Loading local repo cache");
      await UniTask.WhenAll(config.Repos.Select(repo =>
        UniTask.RunOnThreadPool(() => LoadRepoCache(repo))));

      return config;
    }

    public static async UniTask UpdateRepos(ModReposConfig config)
    {
      Logger.Global.LogDebug("Fetching repo updates");
      await UniTask.WhenAll(config.Repos.Select(repo => UpdateRepoData(repo)));
    }

    public static void AssignNewRepoDir(ModReposConfig config, ModRepoDef repo)
    {
      var dirs = new DirAssignment();
      foreach (var r in config.Repos)
        dirs.Assign(r.DirName, r.ID);
      repo.DirName = dirs.Assign(repo.DirName, repo.ID);
    }

    private static readonly XmlSerializer DataSerializer =
      new XmlSerializer(typeof(ModRepoData));
    private static void LoadRepoCache(ModRepoDef repo)
    {
      repo.Data = null;
      var path = repo.LocalDataPath;
      if (string.IsNullOrEmpty(repo.Digest) || !File.Exists(path))
      {
        Logger.Global.LogDebug($"{repo.ID}: no cached file or digest");
        return;
      }

      try
      {
        var rawData = File.ReadAllBytes(path);
        var digest = DataUtils.DigestSHA256(rawData);
        if (digest != repo.Digest)
        {
          Logger.Global.LogDebug($"{repo.ID}: digest mismatch");
          return;
        }

        using var reader = new MemoryStream(rawData);
        repo.Data = DataSerializer.Deserialize(reader) as ModRepoData;
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
        repo.Data = null;
      }
    }

    public static async UniTask UpdateRepoData(ModRepoDef repo)
    {
      var lastFetchSeconds = DateTime.UtcNow.Subtract(repo.LastFetch).TotalSeconds;
      if (repo.Data != null && lastFetchSeconds < Configs.RepoUpdateFrequency.Value)
      {
        Logger.Global.LogDebug($"{repo.ID}: up to date");
        return;
      }

      if (repo.Data == null && repo.HasCacheKey)
      {
        Logger.Global.LogDebug($"{repo.ID}: no data. clearing cache key");
        repo.SetCacheKey(null);
      }

      var res = await repo.FetchRemote();
      if (res.UseCache)
      {
        // no changes. just update LastFetch
        repo.LastFetch = DateTime.UtcNow;
        Logger.Global.LogDebug($"{repo.ID}: no change");
        return;
      }
      if (res.Data == null)
      {
        // fetch failed, keep everything as-is
        Logger.Global.LogDebug($"{repo.ID}: fetch failed");
        return;
      }

      ModRepoData newData;
      try
      {
        using var stream = new MemoryStream(res.Data);
        newData = DataSerializer.Deserialize(stream) as ModRepoData;
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
        // on deserialize failure, keep existing data
        return;
      }

      if (!Directory.Exists(repo.LocalDirPath))
        Directory.CreateDirectory(repo.LocalDirPath);

      try
      {
        File.WriteAllBytes(repo.LocalDataPath, res.Data);
      }
      catch (Exception ex)
      {
        Logger.Global.LogError($"failed to save {repo.LocalDataPath}: {ex.Message}");
        return;
      }

      repo.Data = newData;
      repo.Digest = DataUtils.DigestSHA256(res.Data);
      repo.LastFetch = DateTime.UtcNow;
      repo.SetCacheKey(res.CacheKey);
    }

    public static List<RepoModUpdateTarget> GetModUpdateTargets(ModReposConfig config)
    {
      var index = ModRepoIndex.Build(config);
      var dirs = InitRepoModsAssignment(config);

      var targets = new List<RepoModUpdateTarget>();
      foreach (var mod in config.Mods)
      {
        if (TryPickModUpdate(index, dirs, mod, out var update))
          targets.Add(update);
      }

      return targets;
    }

    public static async UniTask UpdateMods(ModReposConfig config, List<RepoModUpdateTarget> updates)
    {
      var index = ModRepoIndex.Build(config);
      var dirs = InitRepoModsAssignment(config);

      await UniTask.WhenAll(updates.Select(u => PerformModUpdate(u)));

      CleanRepoModDirs(config);
    }

    public static async UniTask<bool> UpdateMod(ModReposConfig config, RepoModDef mod)
    {
      var index = ModRepoIndex.Build(config);
      var dirs = InitRepoModsAssignment(config);
      if (!TryPickModUpdate(index, dirs, mod, out var update))
        return false;
      return await PerformModUpdate(update);
    }

    private static DirAssignment InitRepoModsAssignment(ModReposConfig config)
    {
      var dirs = new DirAssignment();
      foreach (var mod in config.Mods)
      {
        if (mod.DirName is null)
          continue;
        if (!dirs.CanAssign(mod.DirName))
        {
          Logger.Global.LogDebug($"repomod dir {mod.DirName} already in use");
          mod.DirName = null;
          continue;
        }
        var aboutPath = Path.Join(
          LaunchPadPaths.RepoModsPath, mod.DirName, "About/About.xml");
        if (!File.Exists(aboutPath))
        {
          Logger.Global.LogDebug($"repomod dir {mod.DirName} missing About/About.xml");
          mod.DirName = null;
          continue;
        }
        if (!dirs.TryAssign(mod.DirName))
          throw new InvalidOperationException();
      }
      return dirs;
    }

    private static bool TryPickModUpdate(
      ModRepoIndex index, DirAssignment dirs, RepoModDef mod, out RepoModUpdateTarget update)
    {
      update = null;
      if (mod.Repo == null)
        return false;
      mod.PrevDirName = mod.DirName;
      var target = index.GetLatest(
        mod.ModID, mod.RepoID, mod.Branch, mod.MinVersion, mod.MaxVersion);
      if (target == null)
      {
        Logger.Global.LogWarning(
          $"No valid versions for {mod.ModID}@{mod.Branch}[{mod.MinVersion},{mod.MaxVersion}] in {mod.RepoID}");
        return false;
      }
      if (!string.IsNullOrEmpty(mod.DirName)
        && Version.Compare(target.Version, mod.Version) <= 0)
      {
        Logger.Global.LogDebug($"{mod.ModID}@{mod.Branch}[{mod.Version}] in {mod.RepoID} up to date");
        return false;
      }
      var newDir = dirs.Assign(null, $"{mod.ModID}_{mod.Branch}_{target.Version}");
      update = new() { Mod = mod, Version = target, DirName = newDir };
      return true;
    }

    public static void CleanRepoModDirs(ModReposConfig config)
    {
      if (!Directory.Exists(LaunchPadPaths.RepoModsPath))
        Directory.CreateDirectory(LaunchPadPaths.RepoModsPath);

      var modDirs = new HashSet<string>();
      foreach (var mod in config.Mods)
        if (!string.IsNullOrEmpty(mod.DirName))
          modDirs.Add(mod.DirName);

      foreach (var dirName in Directory.GetDirectories(LaunchPadPaths.RepoModsPath))
      {
        var dir = new DirectoryInfo(dirName);
        if (!dir.Exists || modDirs.Contains(dir.Name))
          continue;
        Logger.Global.LogDebug($"cleaning unused repo mod dir {dir.Name}");
        try { dir.Delete(true); } catch { }
      }
    }

    private static async UniTask<bool> PerformModUpdate(RepoModUpdateTarget update)
    {
      var mod = update.Mod;
      var target = update.Version;
      var dirName = update.DirName;
      var curName = $"{mod.ModID}@{mod.Branch}[{mod.Version}]";
      var targetName = $"{mod.ModID}@{mod.Branch}[{target.Version}]";
      Logger.Global.LogInfo($"Updating {curName} to {target.Version}");
      try
      {
        if (string.IsNullOrEmpty(dirName))
          throw new InvalidOperationException($"empty dirName for {curName} during update");

        using var req = UnityWebRequest.Get(target.Url);
        req.timeout = Configs.RepoModFetchTimeout.Value;
        var res = await req.SendWebRequest();
        if (res.responseCode != 200)
        {
          Logger.Global.LogError($"Error fetching {targetName} from {mod.RepoID}: status {res.responseCode}");
          return false;
        }
        var data = res.downloadHandler.data;

        if (!string.IsNullOrEmpty(target.Digest))
        {
          var digest = DataUtils.DigestSHA256(data);
          if (digest != target.Digest)
          {
            var msg = $"{targetName} from {mod.RepoID} does not match repo digest: {digest} != {target.Digest}";
            if (Configs.RepoModValidateDigest.Value)
            {
              Logger.Global.LogError($"{msg}. skipping update");
              return false;
            }
            Logger.Global.LogWarning(msg);
          }
        }

        var dirPath = Path.Join(LaunchPadPaths.RepoModsPath, dirName);
        if (Directory.Exists(dirPath))
          try { Directory.Delete(dirPath, true); } catch { }

        using var archive = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        archive.ExtractToDirectory(dirPath, true);

        var aboutPath = Path.Join(dirPath, "About/About.xml");
        var about = XmlSerialization.LoadOrNull<ModAboutEx>(aboutPath);
        if (about == null)
        {
          Logger.Global.LogError($"{mod.ModID}@{mod.Branch}[{target.Version}] from {mod.RepoID} does not contain an About/About.xml file. skipping update");
          try { Directory.Delete(dirPath, true); } catch { }
          return false;
        }
        if (about.ModID != target.ModID || about.Version != target.Version)
        {
          var msg = $"{about.ModID}@[{about.Version}] does not match match target {targetName} from {mod.RepoID}";
          if (Configs.RepoModValidateVersion.Value)
          {
            Logger.Global.LogError($"{msg}. skipping update");
            try { Directory.Delete(dirPath, true); } catch { }
            return false;
          }
          Logger.Global.LogWarning(msg);
        }

        Logger.Global.LogInfo($"Updated {mod.ModID}@{mod.Branch}[{mod.Version}] to {target.Version}");
        mod.Version = target.Version;
        mod.DirName = dirName;
        return true;
      }
      catch (Exception ex)
      {
        Logger.Global.LogError($"Error updating {mod.ModID}@{mod.Branch}[{mod.Version}] to {target.Version}");
        Logger.Global.LogException(ex);
        return false;
      }
    }

    public static void UpdateModPaths(ModReposConfig config, ModConfig modConfig)
    {
      var updates = new Dictionary<string, string>();
      foreach (var mod in config.Mods)
      {
        if (string.IsNullOrEmpty(mod.DirName) || string.IsNullOrEmpty(mod.PrevDirName))
          continue;
        updates[Path.Join(LaunchPadPaths.RepoModsPath, mod.PrevDirName)] =
          Path.Join(LaunchPadPaths.RepoModsPath, mod.DirName);
      }

      for (var i = 0; i < modConfig.Mods.Count; i++)
      {
        var mod = modConfig.Mods[i];
        // if we updated this mod, switch it to the new directory
        if (updates.TryGetValue(mod.DirectoryPath, out var newPath))
          modConfig.Mods[i] = new LocalModData(newPath, mod.Enabled);
      }
    }

    private static string ToDirName(string id)
    {
      var name = Platform.MakeValidFileName(id);
      if (name.Length > 64)
        name = name[..64];
      return name;
    }

    private class DirAssignment
    {
      private readonly HashSet<string> used = new();

      public string Assign(string current, string id)
      {
        if (!string.IsNullOrEmpty(current))
        {
          // if it has a valid existing dir that is unused, keep it
          var existing = ToDirName(current);
          if (current == existing && used.Add(current))
            return current;
        }
        // try just using the cleaned id
        current = ToDirName(id);
        if (used.Add(current))
          return current;
        // on conflict start adding numbers to end
        var baseName = current;
        var index = 0;
        while (!used.Add(current = $"{baseName}~{++index}")) ;
        return current;
      }

      public bool TryAssign(string dir)
      {
        if (string.IsNullOrEmpty(dir))
          return false;
        if (dir != ToDirName(dir))
          return false;
        return used.Add(dir);
      }

      public bool CanAssign(string dir)
      {
        if (string.IsNullOrEmpty(dir))
          return false;
        if (dir != ToDirName(dir))
          return false;
        return !used.Contains(dir);
      }
    }
  }
}