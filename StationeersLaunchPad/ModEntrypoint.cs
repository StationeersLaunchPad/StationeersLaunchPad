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
    public readonly GameObject Prefab;
    public GameObject Instance;
    public ModBehaviour[] Behaviours;

    public PrefabEntrypoint(GameObject prefab) => this.Prefab = prefab;

    public override string DebugName() => $"Prefab Entry {this.Prefab.name}";

    public override void Instantiate(GameObject parent)
    {
      this.Instance = GameObject.Instantiate(this.Prefab, parent.transform);
      this.Behaviours = this.Instance.GetComponents<ModBehaviour>();
    }

    public override void Initialize(LoadedMod mod)
    {
      foreach (var behaviour in this.Behaviours)
      {
        behaviour.contentHandler = mod.ContentHandler;
        behaviour.OnLoaded(mod.ContentHandler);
      }
    }

    public override IEnumerable<ConfigFile> Configs()
    {
      foreach (var behaviour in this.Behaviours)
      {
        if (behaviour.Config != null)
          yield return behaviour.Config;
      }
    }
  }

  public abstract class BehaviourEntrypoint<T> : ModEntrypoint where T : MonoBehaviour
  {
    public readonly Type Type;
    public T Instance;

    public BehaviourEntrypoint(LoadedAssembly assembly, TypeDefinition type)
    {
      this.Type = assembly.Assembly.GetType(type.FullName);
    }
  }

  public class StationeersModsEntrypoint : BehaviourEntrypoint<ModBehaviour>
  {
    public StationeersModsEntrypoint(LoadedAssembly assembly, TypeDefinition type) : base(assembly, type) { }

    public override string DebugName() => $"StationeersMods Entry {this.Type.FullName}";

    public override void Instantiate(GameObject parent)
    {
      this.Instance = (ModBehaviour) parent.AddComponent(this.Type);
    }

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
    public BepinexEntrypoint(LoadedAssembly assembly, TypeDefinition type) : base(assembly, type) { }

    public override string DebugName() => $"BepInEx Entry {this.Type.FullName}";

    public override void Instantiate(GameObject parent)
    {
      this.Instance = (BaseUnityPlugin) parent.AddComponent(this.Type);
    }

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
    public readonly MethodInfo LoadMethod;

    public DefaultEntrypoint(LoadedAssembly assembly, TypeDefinition type) : base(assembly, type)
    {
      this.LoadMethod = this.Type.GetMethod(
        ModLoader.DEFAULT_METHOD,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        null,
        new Type[] { typeof(List<GameObject>) },
        null
      );
    }

    public override string DebugName() => $"Default Entry {this.Type.FullName}";

    public override void Instantiate(GameObject parent)
    {
      this.Instance = (MonoBehaviour) parent.AddComponent(this.Type);
    }

    public override void Initialize(LoadedMod mod)
    {
      this.LoadMethod.Invoke(this.Instance, new object[] { mod.Prefabs });
    }

    public override IEnumerable<ConfigFile> Configs()
    {
      // no configs on default entrypoints yet
      yield break;
    }
  }
}