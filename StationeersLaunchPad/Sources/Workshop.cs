
using System.Collections.Generic;
using System.IO;
using Assets.Scripts.Networking.Transports;
using Assets.Scripts.Serialization;
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Metadata;
using Steamworks.Ugc;

namespace StationeersLaunchPad.Sources;

public class WorkshopModSource : ModSource
{
  public static readonly WorkshopModSource Instance = new();
  private WorkshopModSource() { }
  public override async UniTask<List<ModDefinition>> ListMods(ModSourceState state)
  {
    var mods = new List<ModDefinition>();
    if (state.SteamDisabled)
      return Configs.RetainWorkshopMods.Value ? ListRetainedMods() : mods;

    var items = await Steam.LoadWorkshopItems();

    foreach (var item in items)
    {
      var about = LoadAbout(Path.Join(item.Directory, "About/About.xml"), item.Title);
      mods.Add(new WorkshopModDefinition(item, about));
    }

    return mods;
  }

  private static List<ModDefinition> ListRetainedMods()
  {
    var config = ModConfigUtil.LoadConfig();
    var mods = new List<ModDefinition>();
    foreach (var mod in config.Mods)
    {
      if (mod is not WorkshopModData wmod)
        continue;
      if (!File.Exists(mod.AboutXmlPath))
        continue;
      var about = LoadAbout(mod.AboutXmlPath, mod.DirectoryPath.Value);
      mods.Add(new FakeWorkshopModDefinition(mod.DirectoryPath, wmod.WorkshopId, about));
    }
    return mods;
  }

  private static ModAboutEx LoadAbout(string path, string fallbackName) =>
    XmlSerialization.Deserialize<ModAboutEx>(path, "ModMetadata") ?? new()
    {
      Name = $"[Invalid About.xml] {fallbackName}",
      Author = "",
      Version = "",
      Description = "",
    };
}

public class WorkshopModDefinition(Item item, ModAboutEx about) : ModDefinition(about)
{
  public readonly Item Item = item;

  public override ModSourceType Type => ModSourceType.Workshop;
  public override ulong WorkshopHandle => Item.Id;
  public override string DirectoryPath => Item.Directory;
  public override ModData ToModData(bool enabled) => new WorkshopModData(
    SteamTransport.ItemWrapper.WrapWorkshopItem(Item, "About/About.xml"),
    enabled
  );
}

public class FakeWorkshopModDefinition(string dir, ulong handle, ModAboutEx about) : ModDefinition(about)
{
  public override ModSourceType Type => ModSourceType.Workshop;
  public override ulong WorkshopHandle { get; } = handle;
  public override string DirectoryPath { get; } = dir;

  public override ModData ToModData(bool enabled) => new WorkshopModData()
  {
    Enabled = enabled,
    DirectoryPath = new(DirectoryPath),
    WorkshopId = new(WorkshopHandle),
  };
}