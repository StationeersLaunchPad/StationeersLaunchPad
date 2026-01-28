
using BepInEx;
using BepInEx.Configuration;
using StationeersLaunchPad.Loading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace StationeersLaunchPad.Entrypoints
{
  public class DefaultEntrypoint : BehaviourEntrypoint<MonoBehaviour>
  {
    private const string DEFAULT_METHOD_NAME = "OnLoaded";

    public readonly LoadedMod Mod;
    public readonly MethodInfo LoadMethod;
    public readonly List<EntrypointParam> Params;

    private DefaultEntrypoint(
      LoadedMod mod, Type type,
      MethodInfo loadMethod, List<EntrypointParam> eparams) : base(type)
    {
      Mod = mod;
      LoadMethod = loadMethod;
      Params = eparams;
    }

    public override string DebugName() => $"Default Entry {Type.FullName}";

    public override void Instantiate(GameObject parent) =>
      Instance = (MonoBehaviour) parent.AddComponent(Type);

    public override void Initialize(LoadedMod mod)
    {
      var eparams = new object[Params.Count];
      for (var i = 0; i < eparams.Length; i++)
        eparams[i] = Params[i].GetParam(this);
      LoadMethod.Invoke(Instance, eparams);
    }

    public override IEnumerable<ConfigFile> Configs()
    {
      foreach (var p in Params)
      {
        if (p is ConfigParam cp)
          yield return cp.Config;
      }
    }

    public static DefaultEntrypoint Parse(LoadedMod mod, Type type, MethodInfo loadMethod)
    {
      if (loadMethod.Name != DEFAULT_METHOD_NAME)
        return null;
      var mparams = loadMethod.GetParameters();
      if (mparams.Length == 0)
        return null;
      var hasPrefabs = false;
      var hasConfig = false;
      var eparams = new List<EntrypointParam>();
      foreach (var arg in mparams)
      {
        if (arg.ParameterType == typeof(List<GameObject>) && !hasPrefabs)
        {
          eparams.Add(new PrefabsParam());
          hasPrefabs = true;
        }
        else if (arg.ParameterType == typeof(ConfigFile) && !hasConfig)
        {
          eparams.Add(new ConfigParam());
          hasConfig = true;
        }
        else
          return null;
      }
      return new(mod, type, loadMethod, eparams);
    }

    public abstract class EntrypointParam
    {
      public abstract object GetParam(DefaultEntrypoint entry);
    }
    public class PrefabsParam : EntrypointParam
    {
      public override object GetParam(DefaultEntrypoint entry) => entry.Mod.Prefabs;
    }
    public class ConfigParam : EntrypointParam
    {
      private static HashSet<char> _invalidFileChars;
      private static HashSet<char> InvalidFileChars
      {
        get
        {
          if (_invalidFileChars == null)
          {
            _invalidFileChars = new();
            foreach (var c in Path.GetInvalidFileNameChars())
              _invalidFileChars.Add(c);
          }
          return _invalidFileChars;
        }
      }

      public ConfigFile Config;
      public override object GetParam(DefaultEntrypoint entry)
      {
        var configID = entry.Mod.Info.About.ModID ?? entry.Type.FullName;
        var sb = new StringBuilder();
        foreach (var c in configID)
          sb.Append(InvalidFileChars.Contains(c) ? '_' : c);
        configID = sb.ToString();
        var path = Path.Join(Paths.ConfigPath, $"{configID}.cfg");
        return Config = new ConfigFile(path, true);
      }
    }
  }

  public partial class EntrypointSearch
  {

    private List<ModEntrypoint> FindDefaultEntrypoints()
    {
      var entries = new List<ModEntrypoint>();
      EachTypeSafe(type =>
      {
        if (!typeof(MonoBehaviour).IsAssignableFrom(type))
          return;

        foreach (var method in type.GetMethods(
          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
          try
          {
            var entry = DefaultEntrypoint.Parse(mod, type, method);
            if (entry != null)
            {
              entries.Add(entry);
              break;
            }
          }
          catch (Exception ex)
          {
            logger.LogException(ex);
          }
        }
      });
      return entries;
    }
  }
}