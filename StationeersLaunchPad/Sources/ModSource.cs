
using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Repos;

namespace StationeersLaunchPad.Sources;

public enum ModSourceType
{
  Core,
  Local,
  Workshop,
  Repo,
}

public struct ModSourceState
{
  public bool SteamDisabled;
  public ModReposConfig Repos;
}

public abstract class ModSource
{
  public abstract UniTask<List<ModDefinition>> ListMods(ModSourceState state);

  public static async UniTask<List<ModDefinition>> ListAll(ModSourceState state)
  {
    var listTasks = new List<UniTask<List<ModDefinition>>>()
    {
      CoreModSource.Instance.ListMods(state),
      LocalModSource.Instance.ListMods(state),
      WorkshopModSource.Instance.ListMods(state),
      RepoModSource.Instance.ListMods(state),
    };

    var lists = await UniTask.WhenAll(listTasks);
    var mods = new List<ModDefinition>();
    foreach (var list in lists)
      mods.AddRange(list);
    return mods;
  }
}

// Contains only the base information about a mod, not any configuration related to it
public abstract class ModDefinition(ModAboutEx about)
{
  public readonly ModAboutEx About = about;

  public abstract ModSourceType Type { get; }
  public abstract ulong WorkshopHandle { get; }
  public abstract string DirectoryPath { get; }
  public abstract ModData ToModData(bool enabled);

  // Release/update timestamps used by the mod loader UI for sorting.
  // Workshop mods override these with Steam metadata; otherwise we fall back to
  // the mod folder's filesystem creation/last-write time. Returns null when unknown.
  public virtual DateTime? Created => GetDirTime(lastWrite: false);
  public virtual DateTime? Updated => GetDirTime(lastWrite: true);

  protected DateTime? GetDirTime(bool lastWrite)
  {
    var path = DirectoryPath;
    if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
      return null;
    return lastWrite ? Directory.GetLastWriteTime(path) : Directory.GetCreationTime(path);
  }
}