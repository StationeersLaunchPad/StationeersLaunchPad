
using Assets.Scripts.Networking.Transports;
using Assets.Scripts.Serialization;
using Cysharp.Threading.Tasks;
using Steamworks.Ugc;
using System.Collections.Generic;
using System.IO;

namespace StationeersLaunchPad.Sources
{
  public class WorkshopModSource : ModSource
  {
    public static readonly WorkshopModSource Instance = new();
    private WorkshopModSource() { }
    public override async UniTask<List<ModDefinition>> ListMods()
    {
      var mods = new List<ModDefinition>();

      var items = await Steam.LoadWorkshopItems();

      foreach (var item in items)
      {
        var about = XmlSerialization.Deserialize<ModAbout>(
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

  public class WorkshopModDefinition : ModDefinition
  {
    public readonly Item Item;
    public WorkshopModDefinition(Item item, ModAbout about) : base(about) =>
      Item = item;

    public override string Name => Item.Title;
    public override ModSourceType Type => ModSourceType.Workshop;
    public override ulong WorkshopHandle => Item.Id;
    public override string DirectoryPath => Item.Directory;
    public override ModData ToModData(bool enabled) => new WorkshopModData(
      SteamTransport.ItemWrapper.WrapWorkshopItem(Item, "About/About.xml"),
      enabled
    );
  }
}