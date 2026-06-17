
using System;
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
      return mods;

    List<Item> items;
    try
    {
      items = await Steam.LoadWorkshopItems();
    }
    catch (Exception ex)
    {
      // Never let a Steam Workshop failure abort the whole mod listing: local and repo
      // mods (and the game) can still load without the Workshop ones.
      Logger.Global.LogError("Failed to list Steam Workshop mods; they will be skipped.");
      Logger.Global.LogError("This is usually a Steam issue - try restarting Steam and the game.");
      Logger.Global.LogException(ex);
      return mods;
    }

    foreach (var item in items)
    {
      var about = XmlSerialization.Deserialize<ModAboutEx>(
        Path.Join(item.Directory, "About/About.xml"), "ModMetadata") ?? new()
        {
          Name = $"[Invalid About.xml] {item.Title}",
          Author = "",
          Version = "",
          Description = "",
        };
      mods.Add(new WorkshopModDefinition(item, about));
    }

    return mods;
  }
}

public class WorkshopModDefinition(Item item, ModAboutEx about) : ModDefinition(about)
{
  public readonly Item Item = item;

  public override ModSourceType Type => ModSourceType.Workshop;
  public override ulong WorkshopHandle => Item.Id;
  public override string DirectoryPath => Item.Directory;
  public override System.DateTime? Created => Item.Created;
  public override System.DateTime? Updated => Item.Updated;
  public override ModData ToModData(bool enabled) => new WorkshopModData(
    SteamTransport.ItemWrapper.WrapWorkshopItem(Item, "About/About.xml"),
    enabled
  );
}