using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assets.Scripts.Serialization;
using StationeersLaunchPad.Sources;

namespace StationeersLaunchPad.Metadata;

// one pack is always active and the mod list edits it directly. always-on mods load with
// every pack except Vanilla, server packs are only changed by their server.
// load order is not part of a pack
public class ProfileManager
{
  public const string DefaultPackName = "Default";
  public const string VanillaName = "Vanilla";
  public const string VanillaPlusName = "Vanilla+";

  private List<ProfileData> profiles = [];
  private ProfileData alwaysOn = new() { Name = "Always on" };
  private readonly ProfileData vanilla = new() { Name = VanillaName, Description = "The game without mods." };
  private readonly ProfileData vanillaPlus = new() { Name = VanillaPlusName, Description = "The game with only your always-on mods." };

  public IReadOnlyList<ProfileData> AllProfiles => profiles;
  public IEnumerable<ProfileData> BuiltInPacks => [vanilla, vanillaPlus];
  public IEnumerable<ProfileData> UserPacks => profiles.Where(profile => !IsServerPack(profile));
  public IEnumerable<ProfileData> ServerPacks => profiles.Where(IsServerPack);
  public ProfileData AlwaysOn => alwaysOn;
  public bool IsInitialized { get; private set; }
  public string ActiveProfileName => Configs.ModProfile.Value;
  public ProfileData ActiveProfile => FindProfile(ActiveProfileName);

  public bool IsBuiltIn(ProfileData profile) => profile == vanilla || profile == vanillaPlus;
  public bool IsVanilla(ProfileData profile) => profile == vanilla;
  public static bool IsServerPack(ProfileData profile) => !string.IsNullOrEmpty(profile?.ServerName);
  public bool IsEditable(ProfileData profile) => profile != null && !IsServerPack(profile) && !IsBuiltIn(profile);
  public bool ActiveEditable => IsEditable(ActiveProfile);
  public bool AlwaysOnActive => ActiveProfile != null && !IsVanilla(ActiveProfile);

  private static string AlwaysOnPath => Path.Join(LaunchPadPaths.SavePath, "always-on-mods.xml");

  public void Initialize()
  {
    if (IsInitialized)
      return;

    profiles = ProfileStorage.LoadAll();
    // the built-in names belong to the built-in packs
    profiles.RemoveAll(profile => IsReservedName(profile.Name));
    SortProfiles();
    alwaysOn = LoadAlwaysOn();
    IsInitialized = true;
  }

  public static bool IsReservedName(string name) =>
    VanillaName.Equals(name, StringComparison.OrdinalIgnoreCase)
    || VanillaPlusName.Equals(name, StringComparison.OrdinalIgnoreCase);

  // without an active pack the current mod list becomes the Default pack.
  // returns true if the active pack changed
  public bool EnsureActivePack(ModList modList)
  {
    Initialize();
    if (Platform.IsServer || ActiveProfile != null)
      return false;

    var configured = ActiveProfileName;
    if (!string.IsNullOrEmpty(configured))
      Logger.Global.LogWarning($"Mod pack '{configured}' was not found, using {DefaultPackName}");
    var fallback = FindProfile(DefaultPackName);
    if (fallback == null)
    {
      fallback = new ProfileData
      {
        Name = DefaultPackName,
        Mods = [.. modList.EnabledMods.Where(mod => mod.Source != ModSourceType.Core && !IsAlwaysOn(mod)).Select(CaptureMod)],
      };
      if (!AddPack(fallback))
        return false;
    }
    Configs.ModProfile.Value = fallback.Name;
    return true;
  }

  public bool CreatePack(string name, ProfileData copyFrom = null) =>
    AddPack(new ProfileData
    {
      Name = name,
      Mods = copyFrom == null ? [] : [.. copyFrom.Mods.Select(CloneEntry)],
    });

  public bool CreatePack(string name, IEnumerable<ProfileModEntry> mods) =>
    AddPack(new ProfileData { Name = name, Mods = [.. mods] });

  private bool AddPack(ProfileData profile)
  {
    if (!ProfileStorage.IsValidName(profile.Name) || IsReservedName(profile.Name) || FindProfile(profile.Name) != null)
      return false;
    if (!ProfileStorage.Save(profile))
      return false;
    profiles.Add(profile);
    SortProfiles();
    return true;
  }

  public string UniqueName(string baseName)
  {
    baseName = Platform.MakeValidFileName((baseName ?? "").Trim());
    if (string.IsNullOrWhiteSpace(baseName))
      baseName = "Pack";
    var name = baseName;
    var suffix = 2;
    while ((!ProfileStorage.IsValidName(name) || IsReservedName(name) || FindProfile(name) != null) && suffix < 1000)
      name = $"{baseName} ({suffix++})";
    return name;
  }

  public bool RenamePack(string oldName, string newName)
  {
    var profile = FindProfile(oldName);
    if (profile == null || IsBuiltIn(profile) || !ProfileStorage.IsValidName(newName) || IsReservedName(newName))
      return false;
    var other = FindProfile(newName);
    if (other != null && other != profile)
      return false;
    if (profile.Name == newName)
      return true;

    var wasActive = profile == ActiveProfile;
    var previous = profile.Name;
    profile.Name = newName;
    // the file name is the lower-cased pack name, so a change of case keeps the file
    if (!ProfileStorage.Save(profile))
    {
      profile.Name = previous;
      return false;
    }
    if (!previous.Equals(newName, StringComparison.OrdinalIgnoreCase))
      ProfileStorage.Delete(previous);
    if (wasActive)
      Configs.ModProfile.Value = newName;
    SortProfiles();
    return true;
  }

  public bool DeletePack(string name, ModList modList)
  {
    var profile = FindProfile(name);
    if (profile == null || IsBuiltIn(profile) || !ProfileStorage.Delete(profile.Name))
      return false;

    var wasActive = profile == ActiveProfile;
    profiles.Remove(profile);
    if (wasActive)
    {
      var next = FindProfile(DefaultPackName) ?? UserPacks.FirstOrDefault();
      Configs.ModProfile.Value = next?.Name ?? "";
      if (next == null)
        EnsureActivePack(modList);
      Apply(modList);
    }
    return true;
  }

  public bool ApplyProfile(string profileName, ModList modList)
  {
    var profile = FindProfile(profileName);
    if (profile == null)
      return false;
    Configs.ModProfile.Value = profile.Name;
    Apply(modList);
    return true;
  }

  public void Apply(ModList modList)
  {
    if (ActiveProfile is not { } active)
      return;
    modList.ApplyProfiles(active, IsVanilla(active) ? null : alwaysOn);
    ModConfigUtil.SaveConfig(modList.ToModConfig());
  }

  public bool IsInPack(ModInfo mod, ProfileData profile = null)
  {
    profile ??= ActiveProfile;
    if (profile == null)
      return false;
    if (mod.Source == ModSourceType.Core)
      return true;
    var identity = GetIdentity(mod);
    return profile.Mods.Any(entry => GetIdentity(entry).Equals(identity, StringComparison.OrdinalIgnoreCase));
  }

  public bool IsAlwaysOn(ModInfo mod) =>
    mod.Source != ModSourceType.Core && IsInPack(mod, alwaysOn);

  public bool SetInPack(ModInfo mod, bool include, ModList modList)
  {
    var profile = ActiveProfile;
    if (!IsEditable(profile) || mod.Source == ModSourceType.Core || IsInPack(mod, profile) == include)
      return false;
    if (!SetMembership(profile, mod, include, modList, ProfileStorage.Save))
      return false;
    Apply(modList);
    return true;
  }

  public bool SetAlwaysOn(ModInfo mod, bool on, ModList modList)
  {
    if (mod.Source == ModSourceType.Core || IsAlwaysOn(mod) == on)
      return false;
    if (!SetMembership(alwaysOn, mod, on, modList, SaveAlwaysOn))
      return false;
    Apply(modList);
    return true;
  }

  // only one of stable and beta can load, adding one removes the other
  private static bool SetMembership(
    ProfileData profile, ModInfo mod, bool include, ModList modList, Func<ProfileData, bool> save)
  {
    var remove = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { GetIdentity(mod) };
    if (include && BetaCounterpart(mod, modList) is { } counterpart)
      remove.Add(GetIdentity(counterpart));

    var previous = profile.Mods;
    var mods = profile.Mods.Where(entry => !remove.Contains(GetIdentity(entry))).ToList();
    if (include)
      mods.Add(CaptureMod(mod));
    profile.Mods = mods;
    if (save(profile))
      return true;
    profile.Mods = previous;
    return false;
  }

  // picks up enabled changes made outside the mod list, like switching to a beta
  public void AbsorbEnabledChanges(ModList modList)
  {
    if (ActiveProfile is not { } active)
      return;

    var modIndex = BuildModIndex(modList.AllMods);
    var lists = new List<(ProfileData list, Func<ProfileData, bool> save)>();
    // under Vanilla the always-on mods are off without being removed
    if (AlwaysOnActive)
      lists.Add((alwaysOn, SaveAlwaysOn));
    if (IsEditable(active))
      lists.Insert(0, (active, ProfileStorage.Save));

    var dropped = new Dictionary<ModInfo, ProfileData>();
    foreach (var (list, _) in lists)
      foreach (var entry in list.Mods)
        if (FindMod(entry, modIndex) is { Enabled: false } mod && mod.Source != ModSourceType.Core)
          dropped[mod] = list;

    var added = new List<(ModInfo mod, ProfileData list)>();
    foreach (var mod in modList.EnabledMods)
    {
      if (mod.Source == ModSourceType.Core || IsInPack(mod, active) || IsAlwaysOn(mod))
        continue;
      var counterpart = BetaCounterpart(mod, modList);
      if (counterpart != null && dropped.TryGetValue(counterpart, out var list))
        added.Add((mod, list));
      else if (IsEditable(active))
        added.Add((mod, active));
    }

    foreach (var (list, save) in lists)
    {
      var removed = dropped.Where(pair => pair.Value == list).Select(pair => GetIdentity(pair.Key))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
      var additions = added.Where(pair => pair.list == list).Select(pair => CaptureMod(pair.mod)).ToList();
      if (removed.Count == 0 && additions.Count == 0)
        continue;
      var previous = list.Mods;
      list.Mods = [.. list.Mods.Where(entry => !removed.Contains(GetIdentity(entry))), .. additions];
      if (!save(list))
        list.Mods = previous;
    }
    Apply(modList);
  }

  private static ModInfo BetaCounterpart(ModInfo mod, ModList modList)
  {
    if (modList.IsBetaMod(mod))
      return modList.AllMods.FirstOrDefault(stable => stable.IsBetaProgramFor(mod));
    if (mod.HasBetaProgram)
      return modList.AllMods.FirstOrDefault(beta => beta.WorkshopHandle == mod.BetaWorkshopHandle);
    return null;
  }

  public bool RemoveMod(string profileName, ProfileModEntry entry) =>
    RemoveMods(profileName, [entry]);

  public bool RemoveMods(string profileName, IEnumerable<ProfileModEntry> entries)
  {
    var profile = FindProfile(profileName);
    if (profile == null)
      return false;

    var remove = entries.ToHashSet();
    if (remove.Count == 0 || !profile.Mods.Any(remove.Contains))
      return false;

    var previousMods = profile.Mods;
    profile.Mods = profile.Mods.Where(entry => !remove.Contains(entry)).ToList();
    if (ProfileStorage.Save(profile))
      return true;

    profile.Mods = previousMods;
    return false;
  }

  public bool RemoveAlwaysOn(ProfileModEntry entry, ModList modList)
  {
    var previous = alwaysOn.Mods;
    alwaysOn.Mods = alwaysOn.Mods.Where(pin => pin != entry).ToList();
    if (!SaveAlwaysOn(alwaysOn))
    {
      alwaysOn.Mods = previous;
      return false;
    }
    Apply(modList);
    return true;
  }

  private static ProfileData LoadAlwaysOn()
  {
    var empty = new ProfileData { Name = "Always on" };
    if (!File.Exists(AlwaysOnPath))
      return empty;
    try
    {
      var data = XmlSerialization.Deserialize<ProfileData>(AlwaysOnPath);
      if (data == null)
        return empty;
      data.Name = "Always on";
      return data;
    }
    catch (Exception ex)
    {
      Logger.Global.LogWarning($"Could not read alwaysOn mods: {ex.Message}");
      return empty;
    }
  }

  private static bool SaveAlwaysOn(ProfileData data)
  {
    if (data.SaveXml(AlwaysOnPath))
      return true;
    Logger.Global.LogError($"Failed to save alwaysOn mods to {AlwaysOnPath}");
    return false;
  }

  public List<ProfileModEntry> GetMissingMods(string profileName, ModList modList) =>
    GetMissingMods(FindProfile(profileName), modList);

  public static List<ProfileModEntry> GetMissingMods(ProfileData profile, ModList modList)
  {
    if (profile == null)
      return [];

    var modIndex = BuildModIndex(modList.AllMods);
    return profile.Mods
      .Where(entry => entry.Source != ModSourceType.Core
        && FindMod(entry, modIndex) == null)
      .ToList();
  }

  public static int ModCount(ProfileData profile) =>
    profile?.Mods.Count(entry => entry.Source != ModSourceType.Core) ?? 0;

  public static List<ModInfo> InstalledMods(ProfileData profile, IReadOnlyDictionary<string, ModInfo> modIndex)
  {
    var result = new List<ModInfo>();
    if (profile == null)
      return result;
    foreach (var entry in profile.Mods)
      if (entry.Source != ModSourceType.Core && FindMod(entry, modIndex) is { } mod)
        result.Add(mod);
    return result;
  }

  public ProfileData FindProfile(string profileName)
  {
    if (string.IsNullOrEmpty(profileName))
      return null;
    return BuiltInPacks.Concat(profiles).FirstOrDefault(profile =>
      profile.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
  }

  internal static ModInfo FindMod(ProfileModEntry entry, IEnumerable<ModInfo> mods)
  {
    if (IsPathlessMod(entry))
    {
      if (string.IsNullOrWhiteSpace(entry.ModID))
        return null;
      var matches = mods.Where(mod => mod.Source != ModSourceType.Core
        && entry.ModID.Equals(mod.ModID, StringComparison.OrdinalIgnoreCase))
        .Take(2).ToList();
      return matches.Count == 1 ? matches[0] : null;
    }
    return mods.FirstOrDefault(mod => Matches(entry, mod));
  }

  internal static ModInfo FindMod(
    ProfileModEntry entry, IReadOnlyDictionary<string, ModInfo> modIndex) =>
    modIndex.TryGetValue(GetIdentity(entry), out var mod) ? mod : null;

  internal static Dictionary<string, ModInfo> BuildModIndex(IEnumerable<ModInfo> mods)
  {
    var index = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
    var modList = mods.ToList();
    foreach (var mod in modList)
      index.TryAdd(GetIdentity(mod), mod);
    foreach (var group in modList
      .Where(mod => mod.Source != ModSourceType.Core && !string.IsNullOrWhiteSpace(mod.ModID))
      .GroupBy(mod => mod.ModID, StringComparer.OrdinalIgnoreCase)
      .Where(group => group.Count() == 1))
      index.TryAdd(GetModIdIdentity(group.Key), group.First());
    return index;
  }

  private void SortProfiles() => profiles.Sort((a, b) =>
    string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

  private static ProfileModEntry CaptureMod(ModInfo mod) => new()
  {
    Name = mod.Name ?? "",
    Source = mod.Source,
    DirectoryPath = mod.DirectoryPath ?? "",
    WorkshopHandle = mod.WorkshopHandle,
    ModID = mod.ModID,
  };

  private static ProfileModEntry CloneEntry(ProfileModEntry entry) => new()
  {
    Name = entry.Name,
    Source = entry.Source,
    DirectoryPath = entry.DirectoryPath,
    WorkshopHandle = entry.WorkshopHandle,
    ModID = entry.ModID,
  };

  private static bool Matches(ProfileModEntry entry, ModInfo mod) =>
    GetIdentity(entry).Equals(GetIdentity(mod), StringComparison.OrdinalIgnoreCase);

  private static bool IsPathlessMod(ProfileModEntry entry) =>
    entry.Source != ModSourceType.Core && string.IsNullOrEmpty(entry.DirectoryPath);

  private static string GetModIdIdentity(string modId) => $"SLP2-ModID:{modId}";
  private static string GetWorkshopIdentity(ulong handle) => $"Workshop:{handle}";

  // workshop items are identified by their handle, everything else by its folder.
  // a local copy of a workshop mod has the same handle in its About.xml
  private static string GetIdentity(ProfileModEntry entry) =>
    entry.Source == ModSourceType.Core ? "Core"
    : entry.Source == ModSourceType.Workshop && entry.WorkshopHandle > 1 ? GetWorkshopIdentity(entry.WorkshopHandle)
    : IsPathlessMod(entry) ? GetModIdIdentity(entry.ModID ?? "")
    : NormalizePath(entry.DirectoryPath);

  private static string GetIdentity(ModInfo mod) =>
    mod.Source == ModSourceType.Core ? "Core"
    : mod.Source == ModSourceType.Workshop && mod.WorkshopHandle > 1 ? GetWorkshopIdentity(mod.WorkshopHandle)
    : NormalizePath(mod.DirectoryPath);

  private static string NormalizePath(string path) =>
    path?.Replace("\\", "/").Trim().ToLowerInvariant() ?? "";

}
