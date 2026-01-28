
using System.Collections.Generic;

namespace StationeersLaunchPad.Metadata
{
  public class ModSet
  {
    private Dictionary<ulong, ModInfo> byWorkshopHandle = new();
    private Dictionary<string, ModInfo> byModID = new();
    private HashSet<ModInfo> all = new();

    public void Add(ModInfo mod)
    {
      if (mod.WorkshopHandle > 1)
        byWorkshopHandle[mod.WorkshopHandle] = mod;
      if (!string.IsNullOrEmpty(mod.ModID))
        byModID[mod.ModID] = mod;
      all.Add(mod);
    }

    public bool TryGetExisting(ModInfo mod, out ModInfo existing)
    {
      if (mod.WorkshopHandle > 1 && byWorkshopHandle.TryGetValue(mod.WorkshopHandle, out existing))
        return true;
      if (!string.IsNullOrEmpty(mod.ModID) && byModID.TryGetValue(mod.ModID, out existing))
        return true;
      return all.TryGetValue(mod, out existing);
    }

    public void Remove(ModInfo mod)
    {
      if (mod.WorkshopHandle > 1)
        byWorkshopHandle.Remove(mod.WorkshopHandle);
      if (!string.IsNullOrEmpty(mod.ModID))
        byModID.Remove(mod.ModID);
      all.Remove(mod);
    }
  }
}