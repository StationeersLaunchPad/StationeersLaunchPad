
using Assets.Scripts.Serialization;
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Repos;
using System.Collections.Generic;
using System.IO;

namespace StationeersLaunchPad.Sources
{
  public class RepoModSource : ModSource
  {
    public static readonly RepoModSource Instance = new();
    public override async UniTask<List<ModDefinition>> ListMods(ModSourceState state)
    {
      await UniTask.SwitchToThreadPool();
      var mods = new List<ModDefinition>();

      foreach (var mod in state.Repos.Mods)
      {
        if (string.IsNullOrEmpty(mod.DirName) || string.IsNullOrEmpty(mod.Version))
          continue;
        var aboutPath = Path.Join(
          LaunchPadPaths.RepoModsPath, mod.DirName, "About/About.xml");
        if (!File.Exists(aboutPath))
          continue;
        var about = XmlSerialization.Deserialize<ModAboutEx>(aboutPath, "ModMetadata") ??
          new()
          {
            Name = $"[Invalid About.xml] {mod.ModID}",
            Author = "",
            Version = mod.Version,
            Description = "",
          };
        mods.Add(new RepoModDefinition(mod, about));
      }

      return mods;
    }
  }

  public class RepoModDefinition : ModDefinition
  {
    public readonly RepoModDef Mod;
    public RepoModDefinition(RepoModDef mod, ModAboutEx about) : base(about) =>
      Mod = mod;
    public override ModSourceType Type => ModSourceType.Repo;
    public override ulong WorkshopHandle => About.WorkshopHandle;
    public override string DirectoryPath =>
      Path.Join(LaunchPadPaths.RepoModsPath, Mod.DirName);

    public override ModData ToModData(bool enabled) =>
      new LocalModData(DirectoryPath, enabled);
  }
}