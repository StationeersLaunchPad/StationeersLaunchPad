
using BepInEx.Configuration;
using StationeersLaunchPad.Loading;
using StationeersMods.Interface;
using System.Collections.Generic;
using UnityEngine;

namespace StationeersLaunchPad.Entrypoints
{
  public class PrefabEntrypoint : ModEntrypoint
  {
    public readonly GameObject Prefab;
    public GameObject Instance;
    public ModBehaviour[] Behaviours;

    public PrefabEntrypoint(GameObject prefab) => Prefab = prefab;

    public override string DebugName() => $"Prefab Entry {Prefab.name}";

    public override void Instantiate(GameObject parent)
    {
      Instance = Object.Instantiate(Prefab, parent.transform);
      Behaviours = Instance.GetComponents<ModBehaviour>();
    }

    public override void Initialize(LoadedMod mod)
    {
      foreach (var behaviour in Behaviours)
      {
        behaviour.contentHandler = mod.ContentHandler;
        behaviour.OnLoaded(mod.ContentHandler);
      }
    }

    public override IEnumerable<ConfigFile> Configs()
    {
      foreach (var behaviour in Behaviours)
      {
        if (behaviour.Config != null)
          yield return behaviour.Config;
      }
    }
  }

  public partial class EntrypointSearch
  {
    private List<ModEntrypoint> FindPrefabEntrypoints()
    {
      var entries = new List<ModEntrypoint>();
      var seenPrefabs = new HashSet<GameObject>();
      foreach (var exportSettings in exports)
      {
        var entryPrefab = exportSettings._startupPrefab;
        if (entryPrefab != null && seenPrefabs.Add(entryPrefab))
          entries.Add(new PrefabEntrypoint(entryPrefab));
      }
      return entries;
    }
  }
}