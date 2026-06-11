using System;
using System.Collections.Generic;

namespace StationeersLaunchPad.Metadata;

// Serializable description of a single mod entry in an exported mod list.
public class ModListJsonEntry
{
  public string Name;
  public string ModID;
  public ulong WorkshopHandle;
  public string Source;
  public bool Enabled;
  public int Order;
}

// Serializable snapshot of the current mod list, written/read as JSON via the loader UI.
public class ModListJson
{
  public string ExportedBy = LaunchPadInfo.NAME;
  public string Version;
  public DateTime ExportedAt;
  public List<ModListJsonEntry> Mods = [];
}

// Summary of what happened while importing a mod list.
public class ModListImportResult
{
  public int Matched;
  public int Disabled;
  public readonly List<string> Missing = [];
}
