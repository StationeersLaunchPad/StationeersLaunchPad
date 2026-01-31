
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Metadata;
using System.Collections.Generic;

namespace StationeersLaunchPad.Sources
{
  public class CoreModSource : ModSource
  {
    public static readonly CoreModSource Instance = new();
    private CoreModSource() { }
    public override async UniTask<List<ModDefinition>> ListMods() =>
      new() { new CoreModDefinition() };
  }

  public class CoreModDefinition : ModDefinition
  {
    private static ModAboutEx GetModAbout()
    {
      var fromGame = new CoreModData().GetAboutData();
      return new()
      {
        Name = fromGame.Name,
        ModID = "core",
        Author = fromGame.Author,
        Description = fromGame.Description,
        Version = fromGame.Version,
      };
    }

    public CoreModDefinition() : base(GetModAbout()) { }
    public override ModSourceType Type => ModSourceType.Core;
    public override ulong WorkshopHandle => 1;
    public override string DirectoryPath => "";
    public override ModData ToModData(bool enabled) => new CoreModData();
  }
}