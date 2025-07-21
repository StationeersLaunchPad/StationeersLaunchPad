using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using Cysharp.Threading.Tasks;
using StationeersMods.Interface;
using StationeersMods.Shared;
using UnityEngine;

namespace StationeersLaunchPad
{
  public class LoadedMod
  {
    private object _lock = new();

    public ModInfo Info;

    public Logger Logger;

    public List<LoadedAssembly> Assemblies = new();
    public List<GameObject> Prefabs = new();
    public List<ExportSettings> Exports = new();
    public ContentHandler ContentHandler;

    public List<ModEntrypoint> Entrypoints = new();

    public List<ConfigFile> ConfigFiles = new();

    public bool LoadedAssemblies;
    public bool LoadedAssets;
    public bool LoadedEntryPoints;
    public bool LoadFinished;
    public bool LoadFailed;

    public LoadedMod(ModInfo info)
    {
      this.Logger = Logger.Global.CreateChild(info.DisplayName);
      this.Info = info;
      var resource = new DummyResource(info.Path);
      this.ContentHandler = new(resource, new List<IResource>().AsReadOnly(), this.Prefabs.AsReadOnly());
    }

    private async UniTask<LoadedAssembly> LoadAssemblySingle(AssemblyInfo assemblyInfo)
    {
      this.Logger.LogDebug($"Loading Assembly {assemblyInfo.Name}");
      var assembly = await ModLoader.LoadAssembly(assemblyInfo);
      ModLoader.RegisterAssembly(assembly.Assembly, this);
      this.Logger.LogInfo($"Loaded Assembly");
      return assembly;
    }

    public async UniTask LoadAssembliesSerial()
    {
      foreach (var assemblyInfo in this.Info.Assemblies)
        this.Assemblies.Add(await this.LoadAssemblySingle(assemblyInfo));
    }

    public async UniTask LoadAssembliesParallel()
    {
      var assemblies = await UniTask.WhenAll(
        this.Info.Assemblies.Select(assemblyInfo => this.LoadAssemblySingle(assemblyInfo))
      );
      this.Assemblies.AddRange(assemblies);
    }

    private async UniTask LoadAssetsSingle(string path)
    {
      var bundle = await this.LoadAssetBundle(path);
      var prefabs = await this.LoadAssetBundleGameObjects(path, bundle);
      lock (this._lock)
        this.Prefabs.AddRange(prefabs);

      var exportSettings = await this.LoadAssetBundleExportSettings(path, bundle);
      if (exportSettings != null)
        lock (this._lock)
          this.Exports.Add(exportSettings);
    }

    public async UniTask LoadAssetsSerial()
    {
      foreach (var path in this.Info.AssetBundles)
        await this.LoadAssetsSingle(path);
    }

    public async UniTask LoadAssetsParallel()
    {
      await UniTask.WhenAll(this.Info.AssetBundles.Select(path => this.LoadAssetsSingle(path)));
    }

    public UniTask FindEntrypoints()
    {
      return UniTask.RunOnThreadPool(() =>
      {
        this.Logger.LogDebug("Finding Entrypoints");

        // StationeersMods would take any ModBehaviour it found as an entrypoint when there were no ExportSettings
        // We'll do the same to ensure any mods relying on this still work
        var smEntries = this.Exports.Count == 0 ?
            ModLoader.FindAnyStationeersModsEntrypoints(this.Assemblies) :
            ModLoader.FindExplicitStationeersModsEntrypoints(this.Assemblies);

        if (smEntries.Count == 0)
          smEntries = ModLoader.FindExportSettingsClassEntrypoints(this.Assemblies, this.Exports);

        this.Entrypoints.AddRange(smEntries);

        this.Entrypoints.AddRange(ModLoader.FindExportSettingsPrefabEntrypoints(this.Exports));
        this.Entrypoints.AddRange(ModLoader.FindBepInExEntrypoints(this.Assemblies));
        this.Entrypoints.AddRange(ModLoader.FindDefaultEntrypoints(this.Assemblies));

        this.Logger.LogInfo($"Found {this.Entrypoints.Count} Entrypoints");
      });
    }

    public void PrintEntrypoints()
    {
      // getting prefab names fails on a thread in the debug player, so just print all the entrypoints after we finish
      foreach (var entry in this.Entrypoints)
        this.Logger.LogDebug($"- {entry.DebugName()}");
    }


    public void LoadEntrypoints()
    {
      this.Logger.LogDebug("Loading Entrypoints");

      var gameObj = new GameObject();
      gameObj.name = this.Info.DisplayName;
      GameObject.DontDestroyOnLoad(gameObj);

      // instantiate all entrypoints
      foreach (var entrypoint in this.Entrypoints)
        entrypoint.Instantiate(gameObj);

      // initialize all entrypoints
      foreach (var entrypoint in this.Entrypoints)
      {
        entrypoint.Initialize(this);
        this.ConfigFiles.AddRange(entrypoint.Configs());
      }

      foreach (var config in this.ConfigFiles)
        config.SettingChanged += (_, _) => this.DirtyConfig();

      this.ConfigFiles.Sort((a, b) => a.ConfigFilePath.CompareTo(b.ConfigFilePath));

      this.Logger.LogInfo("Loaded Entrypoints");
      this.LoadFinished = true;
    }

    private UniTask<AssetBundle> LoadAssetBundle(string path)
    {
      var name = Path.GetFileName(path);
      this.Logger.LogDebug($"Loading AssetBundle {name}");
      return ModLoader.LoadAssetBundle(path);
    }

    private async UniTask<List<GameObject>> LoadAssetBundleGameObjects(string path, AssetBundle bundle)
    {
      var name = Path.GetFileName(path);
      this.Logger.LogDebug($"Loading AssetBundle {name} Prefabs");
      var assets = await ModLoader.LoadAllBundleAssets(bundle);

      foreach (var asset in assets)
        this.Logger.LogDebug($"- Asset {asset.name}");

      return assets;
    }

    private UniTask<ExportSettings> LoadAssetBundleExportSettings(string path, AssetBundle bundle)
    {
      var name = Path.GetFileName(path);
      this.Logger.LogDebug($"Loading AssetBundle {name} ExportSettings");
      return ModLoader.LoadBundleExportSettings(bundle);
    }

    private bool _configDirty = true;
    private void DirtyConfig() { this._configDirty = true; }

    private List<SortedConfigFile> _cachedSortedConfigs = new();
    private int _cachedTotalConfigs = 0;

    public List<SortedConfigFile> GetSortedConfigs()
    {
      var totalCount = 0;
      foreach (var config in this.ConfigFiles)
        totalCount += config.Count;
      if (this._configDirty || totalCount != this._cachedTotalConfigs)
      {
        var sortedConfigs = new List<SortedConfigFile>();
        foreach (var config in this.ConfigFiles)
          if (config.Count > 0)
            sortedConfigs.Add(new SortedConfigFile(config));

        this._cachedTotalConfigs = totalCount;
        this._cachedSortedConfigs = sortedConfigs;
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
          group.OrderBy(entry => entry.Definition.Key).ToList()
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