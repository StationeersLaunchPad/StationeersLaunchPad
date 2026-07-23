using System;
using System.Collections.Generic;
using System.Linq;
using StationeersLaunchPad.Sources;

namespace StationeersLaunchPad.Metadata;

public class ProfileManager
{
  public const string VanillaProfileName = "Vanilla";

  private List<ProfileData> profiles = [];

  public IReadOnlyList<ProfileData> AllProfiles => profiles;
  public bool IsInitialized { get; private set; }
  public string ActiveProfileName => Configs.ModProfile.Value;
  public ProfileData ActiveProfile => FindProfile(ActiveProfileName);

  public void Initialize()
  {
    if (IsInitialized)
      return;

    profiles = ProfileStorage.LoadAll();
    profiles.RemoveAll(profile => IsVanillaProfile(profile.Name));
    profiles.Add(CreateVanillaProfile());
    SortProfiles();
    IsInitialized = true;

    if (string.IsNullOrEmpty(ActiveProfileName) || ActiveProfile != null)
      return;

    Logger.Global.LogWarning($"Configured mod profile '{ActiveProfileName}' was not found");
    DisableProfiles();
  }

  public bool CreateProfile(string profileName, ModList modList)
  {
    if (IsVanillaProfile(profileName)
      || !ProfileStorage.IsValidName(profileName) || FindProfile(profileName) != null)
      return false;

    var profile = CaptureModList(profileName, modList);
    if (!ProfileStorage.Save(profile))
      return false;

    profiles.Add(profile);
    SortProfiles();
    return true;
  }

  public bool ApplyProfile(string profileName, ModList modList)
  {
    var profile = FindProfile(profileName);
    if (profile == null)
      return false;

    modList.ApplyProfile(profile);
    Configs.ModProfile.Value = profile.Name;
    ModConfigUtil.SaveConfig(modList.ToModConfig());
    return true;
  }

  public bool DeleteProfile(string profileName)
  {
    if (IsVanillaProfile(profileName))
      return false;
    var profile = FindProfile(profileName);
    if (profile == null || !ProfileStorage.Delete(profile.Name))
      return false;

    var wasActive = profile == ActiveProfile;
    profiles.Remove(profile);
    if (wasActive)
      DisableProfiles();
    return true;
  }

  public bool UpdateProfile(string profileName, ModList modList)
  {
    if (IsVanillaProfile(profileName))
      return false;
    var profile = FindProfile(profileName);
    if (profile == null)
      return false;

    var previousMods = profile.Mods;
    profile.Mods = CaptureEnabledMods(modList);
    if (!ProfileStorage.Save(profile))
    {
      profile.Mods = previousMods;
      return false;
    }

    return true;
  }

  public bool RemoveMod(string profileName, ProfileModEntry entry)
  {
    return RemoveMods(profileName, [entry]);
  }

  public bool RemoveMods(string profileName, IEnumerable<ProfileModEntry> entries)
  {
    if (IsVanillaProfile(profileName))
      return false;
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

  public void DisableProfiles()
  {
    Configs.ModProfile.Value = "";
  }

  public bool HasDiverged(string profileName, ModList modList)
    => HasDiverged(profileName, modList, BuildModIndex(modList.AllMods));

  internal bool HasDiverged(
    string profileName, ModList modList, IReadOnlyDictionary<string, ModInfo> modIndex)
  {
    var profile = FindProfile(profileName);
    if (profile == null)
      return false;

    var current = modList.EnabledMods.Select(GetIdentity);
    var saved = profile.Mods
      .Select(entry => FindMod(entry, modIndex))
      .Where(mod => mod != null)
      .Select(GetIdentity);
    return !current.SequenceEqual(saved, StringComparer.OrdinalIgnoreCase);
  }

  public List<ProfileModEntry> GetMissingMods(string profileName, ModList modList)
  {
    var profile = FindProfile(profileName);
    if (profile == null)
      return [];

    var modIndex = BuildModIndex(modList.AllMods);
    return profile.Mods
      .Where(entry => GetIdentity(entry) != "Core"
        && FindMod(entry, modIndex) == null)
      .ToList();
  }

  public static bool IsVanillaProfile(string profileName) =>
    VanillaProfileName.Equals(profileName, StringComparison.OrdinalIgnoreCase);

  public ProfileData FindProfile(string profileName)
  {
    if (string.IsNullOrEmpty(profileName))
      return null;
    return profiles.FirstOrDefault(profile =>
      profile.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
  }

  internal static ModInfo FindMod(ProfileModEntry entry, IEnumerable<ModInfo> mods) =>
    mods.FirstOrDefault(mod => Matches(entry, mod));

  internal static ModInfo FindMod(
    ProfileModEntry entry, IReadOnlyDictionary<string, ModInfo> modIndex) =>
    modIndex.TryGetValue(GetIdentity(entry), out var mod) ? mod : null;

  internal static Dictionary<string, ModInfo> BuildModIndex(IEnumerable<ModInfo> mods)
  {
    var index = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
    foreach (var mod in mods)
      index.TryAdd(GetIdentity(mod), mod);
    return index;
  }

  private static ProfileData CaptureModList(string profileName, ModList modList) => new()
  {
    Name = profileName,
    Mods = CaptureEnabledMods(modList),
  };

  private static List<ProfileModEntry> CaptureEnabledMods(ModList modList) =>
    [.. modList.EnabledMods.Select(CaptureMod)];

  private static ProfileData CreateVanillaProfile() => new()
  {
    Name = VanillaProfileName,
    Description = "Launch Stationeers without mods.",
    Mods =
    [
      new()
      {
        Name = "Core",
        Source = ModSourceType.Core,
        DirectoryPath = "",
      },
    ],
  };

  private void SortProfiles() => profiles.Sort((a, b) =>
  {
    var aVanilla = IsVanillaProfile(a.Name);
    var bVanilla = IsVanillaProfile(b.Name);
    if (aVanilla != bVanilla)
      return aVanilla ? -1 : 1;
    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
  });

  private static ProfileModEntry CaptureMod(ModInfo mod) => new()
  {
    Name = mod.Name ?? "",
    Source = mod.Source,
    DirectoryPath = mod.DirectoryPath ?? "",
    WorkshopHandle = mod.WorkshopHandle,
    ModID = mod.ModID,
  };

  private static bool Matches(ProfileModEntry entry, ModInfo mod) =>
    GetIdentity(entry).Equals(GetIdentity(mod), StringComparison.OrdinalIgnoreCase);

  private static string GetIdentity(ProfileModEntry entry) =>
    string.IsNullOrEmpty(entry.DirectoryPath) && entry.WorkshopHandle <= 1
      ? "Core"
      : NormalizePath(entry.DirectoryPath);

  private static string GetIdentity(ModInfo mod) =>
    mod.Source == ModSourceType.Core ? "Core" : NormalizePath(mod.DirectoryPath);

  private static string NormalizePath(string path) =>
    path?.Replace("\\", "/").Trim().ToLowerInvariant() ?? "";

}
