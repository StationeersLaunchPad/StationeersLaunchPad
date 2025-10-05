using BepInEx.Configuration;
using Cysharp.Threading.Tasks;
using StationeersMods.Interface;
using StationeersMods.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace StationeersLaunchPad
{
  public class LoadedMod
  {
    private readonly object _lock = new();
    private bool _configDirty = true;
    private List<SortedConfigFile> _cachedSortedConfigs = [];
    private int _cachedTotalConfigs;

    public ModInfo Info;
    public Logger Logger;

    public List<LoadedAssembly> Assemblies = [];
    public List<GameObject> Prefabs = [];
    public List<ExportSettings> Exports = [];
    public ContentHandler ContentHandler;

    public List<ModEntrypoint> Entrypoints = [];
    public List<ConfigFile> ConfigFiles = [];

    public bool LoadedAssemblies;
    public bool LoadedAssets;
    public bool LoadedEntryPoints;
    public bool LoadFinished;
    public bool LoadFailed;

    public LoadedMod(ModInfo info, Logger logger = null)
    {
      this.Info = info;
      this.Logger = logger ?? Logger.Global.CreateChild(info.DisplayName);

      var resource = new DummyResource(info.Path);
      this.ContentHandler = new(resource, new List<IResource>().AsReadOnly(), this.Prefabs.AsReadOnly());
    }

    private async UniTask<LoadedAssembly> LoadAssemblySingle(AssemblyInfo info)
    {
      this.Logger.LogDebug($"Loading Assembly {info.Name}");
      var assembly = await ModLoader.LoadAssembly(info);
      ModLoader.RegisterAssembly(assembly.Assembly, this);
      this.Logger.LogDebug("Loaded Assembly");
      return assembly;
    }

    public async UniTask LoadAssembliesSerial()
    {
      foreach (var info in this.Info.Assemblies)
        this.Assemblies.Add(await this.LoadAssemblySingle(info));
      this.LoadedAssemblies = true;
    }

    public async UniTask LoadAssembliesParallel()
    {
      var assemblies = await UniTask.WhenAll(this.Info.Assemblies.Select(this.LoadAssemblySingle));
      this.Assemblies.AddRange(assemblies);
      this.LoadedAssemblies = true;
    }

    private async UniTask LoadAssetsSingle(string path)
    {
      var bundle = await ModLoader.LoadAssetBundle(path);
      var prefabs = await ModLoader.LoadAllBundleAssets(bundle);
      var exports = await ModLoader.LoadBundleExportSettings(bundle);

      lock (this._lock)
      {
        this.Prefabs.AddRange(prefabs);
        if (exports != null)
          this.Exports.Add(exports);
      }
    }

    public async UniTask LoadAssetsSerial()
    {
      foreach (var path in this.Info.AssetBundles)
        await this.LoadAssetsSingle(path);
      this.LoadedAssets = true;
    }

    public async UniTask LoadAssetsParallel() =>
        await UniTask.WhenAll(this.Info.AssetBundles.Select(this.LoadAssetsSingle));

    public UniTask FindEntrypoints() => UniTask.RunOnThreadPool(() =>
    {
      this.Logger.LogDebug("Finding Entrypoints");

      var smEntries = this.Exports.Count == 0
          ? ModLoader.FindAnyStationeersModsEntrypoints(this.Assemblies)
          : ModLoader.FindExplicitStationeersModsEntrypoints(this.Assemblies);

      if (smEntries.Count == 0)
        smEntries = ModLoader.FindExportSettingsClassEntrypoints(this.Assemblies, this.Exports);

      this.Entrypoints.AddRange(smEntries);
      this.Entrypoints.AddRange(ModLoader.FindExportSettingsPrefabEntrypoints(this.Exports));
      this.Entrypoints.AddRange(ModLoader.FindBepInExEntrypoints(this.Assemblies));
      this.Entrypoints.AddRange(ModLoader.FindDefaultEntrypoints(this.Assemblies));

      this.Logger.LogDebug($"Found {this.Entrypoints.Count} Entrypoints");
    });

    public void PrintEntrypoints()
    {
      foreach (var entry in this.Entrypoints)
        this.Logger.LogDebug($"- {entry.DebugName()}");
    }

    public void LoadEntrypoints()
    {
      this.Logger.LogDebug("Loading Entrypoints");

      var gameObj = new GameObject(this.Info.DisplayName);
      GameObject.DontDestroyOnLoad(gameObj);

      // Instantiate all entrypoints
      foreach (var entry in this.Entrypoints)
        entry.Instantiate(gameObj);

      // Initialize and gather configs
      foreach (var entry in this.Entrypoints)
      {
        entry.Initialize(this);
        this.ConfigFiles.AddRange(entry.Configs());
      }

      foreach (var config in this.ConfigFiles)
        config.SettingChanged += (_, _) => this._configDirty = true;

      this.ConfigFiles.Sort((a, b) => string.CompareOrdinal(a.ConfigFilePath, b.ConfigFilePath));

      this.Logger.LogInfo("Loaded Mod");
      this.LoadFinished = true;
    }

    public List<SortedConfigFile> GetSortedConfigs()
    {
      var totalCount = this.ConfigFiles.Sum(c => c.Count);

      if (this._configDirty || totalCount != this._cachedTotalConfigs)
      {
        var sortedConfigs = new List<SortedConfigFile>(this.ConfigFiles.Count);
        foreach (var config in this.ConfigFiles)
        {
          if (config.Count > 0)
            sortedConfigs.Add(new SortedConfigFile(config));
        }

        this._cachedSortedConfigs = sortedConfigs;
        this._cachedTotalConfigs = totalCount;
        this._configDirty = false;
      }

      return this._cachedSortedConfigs;
    }
  }

  public class SortedConfigFile
  {
    public readonly ConfigFile ConfigFile;
    public readonly string FileName;
    public readonly List<SortedConfigCategory> Categories;

    public SortedConfigFile(ConfigFile configFile)
    {
      this.ConfigFile = configFile;
      this.FileName = Path.GetFileName(configFile.ConfigFilePath);
      var categories = new List<SortedConfigCategory>();
      foreach (var group in configFile.Select(entry => entry.Value).GroupBy(entry => entry.Definition.Section))
      {
        categories.Add(new SortedConfigCategory(
          configFile,
          group.Key,
          [.. group.OrderBy(entry => entry.Definition.Key)]
        ));
      }
      categories.Sort((a, b) => a.Category.CompareTo(b.Category));
      this.Categories = categories;
    }
  }

  public class SortedConfigCategory
  {
    public readonly ConfigFile ConfigFile;
    public readonly string Category;
    public readonly List<ConfigEntryBase> Entries;

    public SortedConfigCategory(ConfigFile configFile, string category, List<ConfigEntryBase> entries)
    {
      this.ConfigFile = configFile;
      this.Category = category;
      this.Entries = entries;
    }
  }
}