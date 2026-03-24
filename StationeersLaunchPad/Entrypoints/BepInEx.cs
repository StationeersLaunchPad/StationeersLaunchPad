
using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using StationeersLaunchPad.Loading;
using StationeersMods.Interface;
using UnityEngine;

namespace StationeersLaunchPad.Entrypoints;

public class BepInExEntrypoint(Type type) : BehaviourEntrypoint<BaseUnityPlugin>(type)
{
  public override string DebugName() => $"BepInEx Entry {Type.FullName}";

  public override void Instantiate(GameObject parent) =>
    Instance = (BaseUnityPlugin)parent.AddComponent(Type);

  public override void Initialize(LoadedMod mod) { }

  public override IEnumerable<ConfigFile> Configs()
  {
    if (Instance.Config != null)
      yield return Instance.Config;
  }
}

public partial class EntrypointSearch
{
  private List<ModEntrypoint> FindBepInExEntrypoints()
  {
    var entries = new List<ModEntrypoint>();
    var pluginType = typeof(BaseUnityPlugin);
    var smType = typeof(ModBehaviour);
    EachTypeSafe(type =>
    {
      if (pluginType.IsAssignableFrom(type) && !smType.IsAssignableFrom(type))
        entries.Add(new BepInExEntrypoint(type));
    });
    return entries;
  }
}