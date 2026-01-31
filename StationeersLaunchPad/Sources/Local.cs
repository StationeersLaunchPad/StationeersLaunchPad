
using Assets.Scripts.Networking.Transports;
using Assets.Scripts.Serialization;
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Metadata;
using System.Collections.Generic;
using System.IO;

namespace StationeersLaunchPad.Sources
{
  public class LocalModSource : ModSource
  {
    public static readonly LocalModSource Instance = new();
    private LocalModSource() { }
    public override UniTask<List<ModDefinition>> ListMods() =>
      UniTask.RunOnThreadPool(() =>
      {
        var type = SteamTransport.WorkshopType.Mod;
        var localDir = type.GetLocalDirInfo();
        var fileName = type.GetLocalFileName();
        var mods = new List<ModDefinition>();

        if (!localDir.Exists)
        {
          Logger.Global.LogWarning("local mod folder not found");
          return mods;
        }

        foreach (var dir in localDir.GetDirectories("*", SearchOption.AllDirectories))
        {
          foreach (var file in dir.GetFiles(fileName))
          {
            var modDir = dir.Parent;
            var about = XmlSerialization.Deserialize<ModAboutEx>(
              file.FullName, "ModMetadata") ?? new()
              {
                Name = $"[Invalid About.xml] {modDir.Name}",
                Author = "",
                Version = "",
                Description = "",
              };
            mods.Add(new LocalModDefinition(modDir.FullName, about));
          }
        }
        return mods;
      });
  }

  public class LocalModDefinition : ModDefinition
  {
    public readonly string ModDirectory;
    public LocalModDefinition(string modDir, ModAboutEx about) : base(about) =>
      ModDirectory = modDir;
    public override ModSourceType Type => ModSourceType.Local;
    public override ulong WorkshopHandle => About.WorkshopHandle;
    public override string DirectoryPath => ModDirectory;
    public override ModData ToModData(bool enabled) =>
      new LocalModData(ModDirectory, enabled);
  }
}