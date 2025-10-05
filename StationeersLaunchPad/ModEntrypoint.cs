using BepInEx;
using BepInEx.Configuration;
using Mono.Cecil;
using StationeersMods.Interface;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace StationeersLaunchPad
{
  public abstract class ModEntrypoint
  {
    public abstract string DebugName();
    public abstract void Instantiate(GameObject parent);
    public abstract void Initialize(LoadedMod mod);
    public abstract IEnumerable<ConfigFile> Configs();
  }

  public class PrefabEntrypoint : ModEntrypoint
  {
    private readonly GameObject _prefab;
    private ModBehaviour[] _behaviours;
    public GameObject Instance
    {
      get; private set;
    }

    public PrefabEntrypoint(GameObject prefab) => this._prefab = prefab;

    public override string DebugName() => $"Prefab Entry {this._prefab.name}";

    public override void Instantiate(GameObject parent)
    {
      this.Instance = GameObject.Instantiate(this._prefab, parent.transform);
      this._behaviours = this.Instance.GetComponents<ModBehaviour>();
    }

    public override void Initialize(LoadedMod mod)
    {
      if (this._behaviours == null)
        return;

      foreach (var behaviour in this._behaviours)
      {
        behaviour.contentHandler = mod.ContentHandler;
        behaviour.OnLoaded(mod.ContentHandler);
      }
    }

    public override IEnumerable<ConfigFile> Configs()
    {
      if (this._behaviours == null)
        yield break;

      foreach (var behaviour in this._behaviours)
      {
        if (behaviour.Config != null)
          yield return behaviour.Config;
      }
    }
  }

  public abstract class BehaviourEntrypoint<T> : ModEntrypoint where T : MonoBehaviour
  {
    protected readonly Type Type;
    public T Instance
    {
      get; protected set;
    }

    protected BehaviourEntrypoint(LoadedAssembly assembly, TypeDefinition typeDef) =>
        this.Type = assembly.Assembly.GetType(typeDef.FullName);
  }

  public class StationeersModsEntrypoint : BehaviourEntrypoint<ModBehaviour>
  {
    public StationeersModsEntrypoint(LoadedAssembly assembly, TypeDefinition typeDef)
        : base(assembly, typeDef) { }

    public override string DebugName() => $"StationeersMods Entry {this.Type.FullName}";

    public override void Instantiate(GameObject parent) =>
        this.Instance = (ModBehaviour) parent.AddComponent(this.Type);

    public override void Initialize(LoadedMod mod)
    {
      this.Instance.contentHandler = mod.ContentHandler;
      this.Instance.OnLoaded(mod.ContentHandler);
    }

    public override IEnumerable<ConfigFile> Configs()
    {
      if (this.Instance.Config != null)
        yield return this.Instance.Config;
    }
  }

  public class BepinexEntrypoint : BehaviourEntrypoint<BaseUnityPlugin>
  {
    public BepinexEntrypoint(LoadedAssembly assembly, TypeDefinition typeDef)
        : base(assembly, typeDef) { }

    public override string DebugName() => $"BepInEx Entry {this.Type.FullName}";

    public override void Instantiate(GameObject parent) =>
        this.Instance = (BaseUnityPlugin) parent.AddComponent(this.Type);

    public override void Initialize(LoadedMod mod)
    {
    }

    public override IEnumerable<ConfigFile> Configs()
    {
      if (this.Instance.Config != null)
        yield return this.Instance.Config;
    }
  }

  public class DefaultEntrypoint : BehaviourEntrypoint<MonoBehaviour>
  {
    private readonly MethodInfo _loadMethod;

    public DefaultEntrypoint(LoadedAssembly assembly, TypeDefinition typeDef)
        : base(assembly, typeDef) => this._loadMethod = this.Type.GetMethod(
          ModLoader.DEFAULT_METHOD,
          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
          binder: null,
          types: [typeof(List<GameObject>)],
          modifiers: null
      );

    public override string DebugName() => $"Default Entry {this.Type.FullName}";

    public override void Instantiate(GameObject parent) =>
        this.Instance = (MonoBehaviour) parent.AddComponent(this.Type);

    public override void Initialize(LoadedMod mod) =>
        this._loadMethod?.Invoke(this.Instance, [mod.Prefabs]);

    public override IEnumerable<ConfigFile> Configs() => [];
  }
}