using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Entrypoints;
using StationeersLaunchPad.Metadata;
using StationeersMods.Interface;
using StationeersMods.Shared;
using UnityEngine;

namespace StationeersLaunchPad.Loading;

public class LoadedMod
{
  private readonly object _lock = new();

  public ModInfo Info;

  public Logger Logger;

  public List<Assembly> Assemblies = [];
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

  public LoadedMod(ModInfo info)
  {
    Logger = Logger.Global.CreateChild(info.Name);
    Info = info;
    var resource = new DummyResource(info.DirectoryPath);
    ContentHandler = new(resource, new List<IResource>().AsReadOnly(), Prefabs.AsReadOnly());
  }

  private UniTask<Assembly> LoadAssemblySingle(string path) => UniTask.RunOnThreadPool(() =>
  {
    Logger.LogDebug($"Loading Assembly {path}");
    var assembly = Assembly.LoadFrom(path);
    ModLoader.RegisterAssembly(assembly, this);
    Logger.LogInfo($"Loaded Assembly");
    return assembly;
  });

  public async UniTask LoadAssembliesSerial()
  {
    foreach (var path in Info.Assemblies)
      Assemblies.Add(await LoadAssemblySingle(path));
  }

  public async UniTask LoadAssembliesParallel()
  {
    var assemblies = await UniTask.WhenAll(
      Info.Assemblies.Select(LoadAssemblySingle)
    );
    Assemblies.AddRange(assemblies);
  }

  private async UniTask LoadAssetsSingle(string path)
  {
    var bundle = await LoadAssetBundle(path);
    var prefabs = await LoadAssetBundleGameObjects(path, bundle);
    lock (_lock)
      Prefabs.AddRange(prefabs);

    var exportSettings = await LoadAssetBundleExportSettings(path, bundle);
    if (exportSettings != null)
      lock (_lock)
        Exports.Add(exportSettings);
  }

  public async UniTask LoadAssetsSerial()
  {
    foreach (var path in Info.AssetBundles)
      await LoadAssetsSingle(path);
  }

  public async UniTask LoadAssetsParallel()
  {
    await UniTask.WhenAll(Info.AssetBundles.Select(LoadAssetsSingle));
  }

  public UniTask FindEntrypoints()
  {
    return UniTask.RunOnThreadPool(() =>
    {
      Logger.LogDebug("Finding Entrypoints");

      Entrypoints.AddRange(EntrypointSearch.FindEntrypoints(this, Assemblies, Exports));

      Logger.LogInfo($"Found {Entrypoints.Count} Entrypoints");
    });
  }

  public void PrintEntrypoints()
  {
    // getting prefab names fails on a thread in the debug player, so just print all the entrypoints after we finish
    foreach (var entry in Entrypoints)
      Logger.LogDebug($"- {entry.DebugName()}");
  }


  public void LoadEntrypoints()
  {
    Logger.LogDebug("Loading Entrypoints");

    var gameObj = new GameObject { name = Info.Name };
    Object.DontDestroyOnLoad(gameObj);

    // instantiate all entrypoints
    foreach (var entrypoint in Entrypoints)
      entrypoint.Instantiate(gameObj);

    // initialize all entrypoints
    foreach (var entrypoint in Entrypoints)
    {
      entrypoint.Initialize(this);
      ConfigFiles.AddRange(entrypoint.Configs());
    }

    foreach (var config in ConfigFiles)
      config.SettingChanged += (_, _) => DirtyConfig();

    ConfigFiles.Sort((a, b) => a.ConfigFilePath.CompareTo(b.ConfigFilePath));

    Logger.LogInfo("Loaded Entrypoints");
    LoadFinished = true;
  }

  private UniTask<AssetBundle> LoadAssetBundle(string path)
  {
    var name = Path.GetFileName(path);
    Logger.LogDebug($"Loading AssetBundle {name}");
    return ModLoader.LoadAssetBundle(path);
  }

  private async UniTask<List<GameObject>> LoadAssetBundleGameObjects(string path, AssetBundle bundle)
  {
    var name = Path.GetFileName(path);
    Logger.LogDebug($"Loading AssetBundle {name} Prefabs");
    var assets = await ModLoader.LoadAllBundleAssets(bundle);

    foreach (var asset in assets)
      Logger.LogDebug($"- Asset {asset.name}");

    return assets;
  }

  private UniTask<ExportSettings> LoadAssetBundleExportSettings(string path, AssetBundle bundle)
  {
    var name = Path.GetFileName(path);
    Logger.LogDebug($"Loading AssetBundle {name} ExportSettings");
    return ModLoader.LoadBundleExportSettings(bundle);
  }

  private bool _configDirty = true;
  private void DirtyConfig()
  {
    _configDirty = true;
  }

  private List<SortedConfigFile> _cachedSortedConfigs = [];
  private int _cachedTotalConfigs = 0;

  public List<SortedConfigFile> GetSortedConfigs()
  {
    var totalCount = 0;
    foreach (var config in ConfigFiles)
      totalCount += config.Count;
    if (_configDirty || totalCount != _cachedTotalConfigs)
    {
      var sortedConfigs = new List<SortedConfigFile>();
      foreach (var config in ConfigFiles)
        if (config.Count > 0)
          sortedConfigs.Add(new SortedConfigFile(config));

      _cachedTotalConfigs = totalCount;
      _cachedSortedConfigs = sortedConfigs;
      _configDirty = false;
    }
    return _cachedSortedConfigs;
  }
}