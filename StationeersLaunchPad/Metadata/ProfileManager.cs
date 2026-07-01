using System;
using System.Collections.Generic;
using System.Linq;
using StationeersLaunchPad.Sources;

namespace StationeersLaunchPad.Metadata;

public class ProfileManager
{
  private List<ProfileData> profiles = [];

  public IReadOnlyList<ProfileData> AllProfiles => profiles;
  public bool IsInitialized { get; private set; }
  public string ActiveProfileName => Configs.ModProfile.Value;
  public ProfileData ActiveProfile => FindProfile(ActiveProfileName);
  public bool WasActiveProfileApplied =>
    !string.IsNullOrEmpty(ActiveProfileName)
    && ActiveProfileName.Equals(Configs.AppliedModProfile.Value, StringComparison.OrdinalIgnoreCase);

  public void Initialize()
  {
    if (IsInitialized)
      return;

    profiles = ProfileStorage.LoadAll();
    profiles.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    IsInitialized = true;

    if (string.IsNullOrEmpty(ActiveProfileName) || ActiveProfile != null)
      return;

    Logger.Global.LogWarning($"Configured mod profile '{ActiveProfileName}' was not found");
    DisableProfiles();
  }

  public bool CreateProfile(string profileName, ModList modList)
  {
    if (!ProfileStorage.IsValidName(profileName) || FindProfile(profileName) != null)
      return false;

    var profile = CaptureModList(profileName, modList);
    if (!ProfileStorage.Save(profile))
      return false;

    profiles.Add(profile);
    profiles.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    return true;
  }

  public bool ApplyProfile(string profileName, ModList modList)
  {
    var profile = FindProfile(profileName);
    if (profile == null)
      return false;

    modList.ApplyProfile(profile);
    Configs.ModProfile.Value = profile.Name;
    Configs.AppliedModProfile.Value = profile.Name;
    ModConfigUtil.SaveConfig(modList.ToModConfig());
    return true;
  }

  public bool DeleteProfile(string profileName)
  {
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
    var profile = FindProfile(profileName);
    if (profile == null)
      return false;

    profile.Mods = MergeModList(profile, modList, true);
    return ProfileStorage.Save(profile);
  }

  public bool SyncActiveProfile(ModList modList)
  {
    var profile = ActiveProfile;
    if (profile == null || !WasActiveProfileApplied)
      return false;

    var mods = MergeModList(profile, modList, false);
    if (EntriesEqual(profile.Mods, mods))
      return false;

    profile.Mods = mods;
    ProfileStorage.Save(profile);
    return true;
  }

  public void DisableProfiles()
  {
    Configs.ModProfile.Value = "";
    Configs.AppliedModProfile.Value = "";
  }

  public bool HasDiverged(string profileName, ModList modList)
  {
    var profile = FindProfile(profileName);
    if (profile == null)
      return false;

    var current = modList.EnabledMods.Select(GetIdentity);
    var saved = profile.Mods.Where(mod => mod.Enabled).Select(GetIdentity);
    return !current.SequenceEqual(saved, StringComparer.OrdinalIgnoreCase);
  }

  public List<ModInfo> GetNewMods(string profileName, ModList modList)
  {
    var profile = FindProfile(profileName);
    if (profile == null)
      return [];

    return modList.AllMods
      .Where(mod => profile.Mods.All(entry => !Matches(entry, mod)))
      .ToList();
  }

  public ProfileData FindProfile(string profileName)
  {
    if (string.IsNullOrEmpty(profileName))
      return null;
    return profiles.FirstOrDefault(profile =>
      profile.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
  }

  internal static ModInfo FindMod(ProfileModEntry entry, IEnumerable<ModInfo> mods) =>
    mods.FirstOrDefault(mod => Matches(entry, mod));

  private static List<ProfileModEntry> MergeModList(ProfileData profile, ModList modList, bool addNew)
  {
    var entries = new List<ProfileModEntry>();
    var matched = new HashSet<ProfileModEntry>();
    foreach (var mod in modList.AllMods)
    {
      var existing = profile.Mods.FirstOrDefault(entry => Matches(entry, mod));
      if (existing == null && !addNew)
        continue;

      entries.Add(CaptureMod(mod));
      if (existing != null)
        matched.Add(existing);
    }

    entries.AddRange(profile.Mods.Where(entry => !matched.Contains(entry)));
    return entries;
  }

  private static ProfileData CaptureModList(string profileName, ModList modList) => new()
  {
    Name = profileName,
    Mods = [.. modList.AllMods.Select(CaptureMod)],
  };

  private static ProfileModEntry CaptureMod(ModInfo mod) => new()
  {
    Source = mod.Source,
    DirectoryPath = mod.DirectoryPath ?? "",
    WorkshopHandle = mod.WorkshopHandle,
    ModID = mod.ModID,
    Enabled = mod.Enabled,
  };

  private static bool EntriesEqual(List<ProfileModEntry> left, List<ProfileModEntry> right)
  {
    if (left.Count != right.Count)
      return false;

    return left.Zip(right, (a, b) =>
      a.Enabled == b.Enabled
      && GetIdentity(a).Equals(GetIdentity(b), StringComparison.OrdinalIgnoreCase)
    ).All(equal => equal);
  }

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
