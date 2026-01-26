
using BepInEx.Configuration;
using StationeersLaunchPad.Loading;
using StationeersMods.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace StationeersLaunchPad.Entrypoints
{

  public class StationeersModsEntrypoint : BehaviourEntrypoint<ModBehaviour>
  {
    public StationeersModsEntrypoint(Type type) : base(type) { }

    public override string DebugName() => $"StationeersMods Entry {Type.FullName}";

    public override void Instantiate(GameObject parent) =>
      Instance = (ModBehaviour) parent.AddComponent(Type);

    public override void Initialize(LoadedMod mod)
    {
      Instance.contentHandler = mod.ContentHandler;
      Instance.OnLoaded(mod.ContentHandler);
    }

    public override IEnumerable<ConfigFile> Configs()
    {
      if (Instance.Config != null)
        yield return Instance.Config;
    }
  }

  public partial class EntrypointSearch
  {
    private List<ModEntrypoint> FindStationeersModsEntrypoints()
    {
      // StationeersMods would take any ModBehaviour it found as an entrypoint when there were no ExportSettings
      // We'll do the same to ensure any mods relying on this still work
      var entries = exports.Count == 0 ? FindAnySM() : FindExplicitSM();
      if (entries.Count == 0)
        entries = FindSMClassExports();

      return entries;
    }

    private List<ModEntrypoint> FindAnySM()
    {
      var entries = new List<ModEntrypoint>();
      EachTypeSafe(type =>
      {
        if (typeof(ModBehaviour).IsAssignableFrom(type))
          entries.Add(new StationeersModsEntrypoint(type));
      });
      return entries;
    }

    private List<ModEntrypoint> FindExplicitSM()
    {
      var entries = new List<ModEntrypoint>();
      EachTypeSafe(type =>
      {
        var attr = type.GetCustomAttributes().FirstOrDefault(
          attr => attr.GetType().FullName == typeof(StationeersMod).FullName);
        if (attr != null && typeof(ModBehaviour).IsAssignableFrom(type))
          entries.Add(new StationeersModsEntrypoint(type));
      });
      return entries;
    }

    private List<ModEntrypoint> FindSMClassExports()
    {
      var entries = new List<ModEntrypoint>();

      var startupClasses = new HashSet<string>();
      foreach (var export in exports)
      {
        if (!string.IsNullOrEmpty(export._startupClass))
          startupClasses.Add(export._startupClass);
      }

      EachTypeSafe(type =>
      {
        if (startupClasses.Contains(type.FullName) &&
            typeof(ModBehaviour).IsAssignableFrom(type))
          entries.Add(new StationeersModsEntrypoint(type));
      });

      return entries;
    }
  }
}