
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Metadata;
using System.Collections.Generic;

namespace StationeersLaunchPad.Sources
{
  public enum ModSourceType
  {
    Core,
    Local,
    Workshop
  }

  public abstract class ModSource
  {
    public abstract UniTask<List<ModDefinition>> ListMods();

    public static async UniTask<List<ModDefinition>> ListAll(bool includeWorkshop)
    {
      var listTasks = new List<UniTask<List<ModDefinition>>>()
      {
        CoreModSource.Instance.ListMods(),
        LocalModSource.Instance.ListMods(),
      };
      if (includeWorkshop)
        listTasks.Add(WorkshopModSource.Instance.ListMods());
      var lists = await UniTask.WhenAll(listTasks);

      var mods = new List<ModDefinition>();
      foreach (var list in lists)
        mods.AddRange(list);
      return mods;
    }
  }

  // Contains only the base information about a mod, not any configuration related to it
  public abstract class ModDefinition
  {
    public readonly ModAboutEx About;

    public ModDefinition(ModAboutEx about) => About = about;

    public abstract string Name { get; }
    public abstract ModSourceType Type { get; }
    public abstract ulong WorkshopHandle { get; }
    public abstract string DirectoryPath { get; }
    public abstract ModData ToModData(bool enabled);
  }
}