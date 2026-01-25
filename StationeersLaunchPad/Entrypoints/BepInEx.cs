
using BepInEx;
using BepInEx.Configuration;
using StationeersMods.Interface;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace StationeersLaunchPad.Entrypoints
{
  public class BepInExEntrypoint : BehaviourEntrypoint<BaseUnityPlugin>
  {
    public BepInExEntrypoint(Type type) : base(type) { }

    public override string DebugName() => $"BepInEx Entry {Type.FullName}";

    public override void Instantiate(GameObject parent) =>
      Instance = (BaseUnityPlugin) parent.AddComponent(Type);

    public override void Initialize(LoadedMod mod)
    {
    }

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
}