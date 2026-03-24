
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using StationeersLaunchPad.Loading;
using StationeersMods.Shared;
using UnityEngine;

namespace StationeersLaunchPad.Entrypoints;

public abstract class ModEntrypoint
{
  public abstract string DebugName();
  public abstract void Instantiate(GameObject parent);
  public abstract void Initialize(LoadedMod mod);
  public abstract IEnumerable<ConfigFile> Configs();
}

public abstract class BehaviourEntrypoint<T>(Type type) : ModEntrypoint where T : MonoBehaviour
{
  public readonly Type Type = type;
  public T Instance;
}

public partial class EntrypointSearch
{
  private readonly LoadedMod mod;
  private readonly Logger logger;
  private readonly List<Assembly> assemblies;
  private readonly List<ExportSettings> exports;
  private readonly List<Type> types = [];

  private EntrypointSearch(
    LoadedMod mod,
    List<Assembly> assemblies,
    List<ExportSettings> exports)
  {
    this.mod = mod;
    logger = mod.Logger;
    this.assemblies = assemblies;
    this.exports = exports;
  }

  public static List<ModEntrypoint> FindEntrypoints(
    LoadedMod mod,
    List<Assembly> assemblies,
    List<ExportSettings> exports
  ) => new EntrypointSearch(mod, assemblies, exports).FindEntrypoints();

  private List<ModEntrypoint> FindEntrypoints()
  {
    foreach (var asm in assemblies)
      types.AddRange(GetTypesSafe(asm));

    var allEntries = new List<ModEntrypoint>();

    allEntries.AddRange(FindStationeersModsEntrypoints());
    allEntries.AddRange(FindPrefabEntrypoints());
    allEntries.AddRange(FindBepInExEntrypoints());
    allEntries.AddRange(FindDefaultEntrypoints());

    return allEntries;
  }

  private IEnumerable<Type> GetTypesSafe(Assembly assembly)
  {
    try
    {
      return assembly.GetTypes();
    }
    catch (ReflectionTypeLoadException ex)
    {
      return ex.Types.Where(t => t != null);
    }
    catch (Exception ex)
    {
      logger.LogException(ex);
      return [];
    }
  }

  private void EachTypeSafe(Action<Type> each)
  {
    foreach (var type in types)
    {
      try
      {
        if (!type.IsAbstract && !type.IsInterface)
          each(type);
      }
      catch (Exception ex)
      {
        logger.LogException(ex);
      }
    }
  }
}