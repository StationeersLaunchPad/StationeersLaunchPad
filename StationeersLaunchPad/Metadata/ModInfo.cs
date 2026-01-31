using StationeersLaunchPad.Sources;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StationeersLaunchPad.Metadata
{
  public class ModInfo
  {
    // Metadata
    public readonly ModDefinition Def;
    public readonly List<string> Assemblies = new();
    public readonly List<string> AssetBundles = new();

    // Validation state
    public readonly bool DepsInvalid;
    public readonly bool OrderInvalid;

    // Configuration State
    public bool Enabled;
    public bool DepsWarned;

    // Definition Accessors
    public ModAboutEx About => Def.About;
    public ModSourceType Source => Def.Type;
    public string Name => About.Name;
    public string DirectoryPath => Def.DirectoryPath;
    public string DirectoryName => new DirectoryInfo(DirectoryPath).Name;
    public ulong WorkshopHandle => Def.WorkshopHandle;
    public string ModID => About.ModID ?? "";

    public string AboutPath => Path.Combine(DirectoryPath, "About");
    public string AboutXmlPath => Path.Combine(AboutPath, "About.xml");
    public string ThumbnailPath => Path.Combine(AboutPath, "thumb.png");
    public string PreviewPath => Path.Combine(AboutPath, "preview.png");

    public ModInfo(ModDefinition def)
    {
      Def = def;

      if (def.Type is ModSourceType.Core)
        return;

      foreach (var dep in def.About.DependsOn ?? new())
        DepsInvalid |= !dep.IsValid;
      foreach (var before in def.About.OrderBefore ?? new())
        OrderInvalid |= !before.IsValid;
      foreach (var after in def.About.OrderAfter ?? new())
        OrderInvalid |= !after.IsValid;

      Assemblies.AddRange(Directory.GetFiles(
        DirectoryPath, "*.dll", SearchOption.AllDirectories));
      AssetBundles.AddRange(Directory.GetFiles(
        DirectoryPath, "*.assets", SearchOption.AllDirectories));
    }

    public bool SortBefore(ModInfo other)
    {
      if (other.About?.OrderAfter?.Any(v => Satisfies(v)) ?? false)
        return true;
      return About?.OrderBefore?.Any(v => other.Satisfies(v)) ?? false;
    }

    public bool Satisfies(ModReference modRef)
    {
      if (modRef.WorkshopHandle != 0 && WorkshopHandle == modRef.WorkshopHandle)
        return true;
      return !string.IsNullOrEmpty(modRef.ModID) && ModID == modRef.ModID;
    }
  }
}