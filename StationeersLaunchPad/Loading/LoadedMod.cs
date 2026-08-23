using System;
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
  public List<AssetBundle> AssetBundles = [];
  public List<GameObject> Prefabs = [];
  public List<ExportSettings> Exports = [];
  public ContentHandler ContentHandler;

  public List<ModEntrypoint> Entrypoints = [];
  private readonly List<ModEntrypoint> _initializedEntrypoints = [];
  private GameObject _entryGameObject;

  public List<ConfigFile> ConfigFiles = [];

  public bool LoadedAssemblies;
  public bool LoadedAssets;
  public bool LoadedEntryPoints;
  public bool LoadFinished;
  public bool LoadFailed;

  public bool CanSafelyUnload =>
    _initializedEntrypoints.Count == 0 ||
    _initializedEntrypoints.All(entrypoint => entrypoint.SafeModeCompatible);
  
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
    lock (_lock)
      AssetBundles.Add(bundle);
    
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
      
      if (Configs.SafeMode.Value && Entrypoints.Count == 0)
        throw new Exception("No Safe Mode compatible entrypoints found");
      
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

    _entryGameObject  = new GameObject { name = Info.Name };
    UnityEngine.Object.DontDestroyOnLoad(_entryGameObject);
    
    // Instantiate entry points
    foreach (var entrypoint in Entrypoints)
      entrypoint.Instantiate(_entryGameObject);

    // Try initializing the entry points
    foreach (var entrypoint in Entrypoints)
    {
      _initializedEntrypoints.Add(entrypoint);

      if (!entrypoint.TryInitialize(this))
        throw new Exception($"Entrypoint {entrypoint.DebugName()} failed to initialize");

      ConfigFiles.AddRange(entrypoint.Configs());
    }

    foreach (var config in ConfigFiles)
      config.SettingChanged += OnConfigSettingChanged;

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

  private void OnConfigSettingChanged(object sender, SettingChangedEventArgs e)
  {
    DirtyConfig();
  }
  
  public bool TryUnload()
  {
    if (!CanSafelyUnload)
      return false;

    foreach (var entrypoint in _initializedEntrypoints)
    {
      try
      {
        if (!entrypoint.CanUnload())
        {
          Logger.LogWarning(
            $"Entrypoint {entrypoint.DebugName()} refused unload"
          );

          return false;
        }
      }
      catch (Exception ex)
      {
        Logger.LogException(ex);
        return false;
      }
    }

    Unload();
    return true;
  }
  
  public void Unload()
  {
    for (var i = _initializedEntrypoints.Count - 1; i >= 0; i--)
    {
      try
      {
        _initializedEntrypoints[i].Unload();
      }
      catch (Exception ex)
      {
        Logger.LogException(ex);
      }
    }

    _initializedEntrypoints.Clear();

    if (_entryGameObject != null)
    {
      UnityEngine.Object.Destroy(_entryGameObject);
      _entryGameObject = null;
    }
    
    foreach (var assetBundle in AssetBundles)
    {
      try
      {
        assetBundle?.Unload(false);
      }
      catch (Exception ex)
      {
        Logger.LogException(ex);
      }
    }
    AssetBundles.Clear();
    
    Prefabs.Clear();
    Exports.Clear();
    Entrypoints.Clear();
    
    foreach (var config in ConfigFiles)
      config.SettingChanged -= OnConfigSettingChanged;
    ConfigFiles.Clear();
    
    LoadedAssemblies = false; // Need to mark the assembly as unloaded in any case.
    LoadedAssets = false;
    LoadedEntryPoints = false;
    LoadFinished = false;
    
    foreach (var assembly in Assemblies)
      ModLoader.UnregisterAssembly(assembly);

    Assemblies.Clear();
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