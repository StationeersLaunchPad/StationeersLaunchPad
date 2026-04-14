using System;
using System.Collections.Generic;
using System.Linq;

namespace StationeersLaunchPad.Metadata;

public class ProfileManager
{
    private List<ProfileData> profiles = [];

    public IReadOnlyList<ProfileData> AllProfiles => profiles;
    public string ActiveProfileName { get; private set; }
    public bool HasDiverged { get; private set; }

    public bool ProfileExists(string profileName) =>
        profiles.Any(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));

    public void Initialize()
    {
        profiles = ProfileStorage.LoadAll();
        ActiveProfileName = ProfileStorage.LoadActiveName();

        if (ActiveProfileName != null && !ProfileExists(ActiveProfileName))
        {
            ActiveProfileName = null;
            ProfileStorage.ClearActiveName();
        }
    }

    public bool CreateProfile(string profileName, ModList modList)
    {
        if (string.IsNullOrWhiteSpace(profileName) || ProfileExists(profileName))
            return false;

        var profile = CaptureModList(profileName, modList);
        ProfileStorage.Save(profile);
        profiles.Add(profile);
        return true;
    }

    public bool LoadProfile(string profileName, ModList modList)
    {
        var profile = profiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
        if (profile == null)
            return false;
        
        modList.ApplyProfile(profile);
        ActiveProfileName = profile.Name;
        HasDiverged = false;
        ProfileStorage.SaveActiveName(ActiveProfileName);
        ModConfigUtil.SaveConfig(modList.ToModConfig());
        return true;
    }

    public bool DeleteProfile(string profileName)
    {
        var profile = profiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
        if (profile == null)
            return false;

        profiles.Remove(profile);
        ProfileStorage.Delete(profileName);

        if (ActiveProfileName != null && ActiveProfileName.Equals(profileName, StringComparison.OrdinalIgnoreCase))
        {
            ActiveProfileName = null;
            ProfileStorage.ClearActiveName();
        }

        return true;
    }

    public bool RenameProfile(string oldProfileName, string newProfileName)
    {
        if (string.IsNullOrWhiteSpace(newProfileName))
            return false;
        
        var existingProfile = profiles.FirstOrDefault(p => p.Name.Equals(oldProfileName, StringComparison.OrdinalIgnoreCase));
        if (existingProfile == null)
            return false;

        if (profiles.Any(p => p != existingProfile && p.Name.Equals(newProfileName, StringComparison.OrdinalIgnoreCase)))
            return false;
        
        var renamedProfile = ProfileStorage.Rename(oldProfileName, newProfileName);
        if (renamedProfile == null)
            return false;
        
        var index = profiles.IndexOf(renamedProfile);
        profiles[index] = renamedProfile;

        if (ActiveProfileName != null && ActiveProfileName.Equals(oldProfileName, StringComparison.OrdinalIgnoreCase))
        {
            ActiveProfileName = renamedProfile.Name;
            ProfileStorage.SaveActiveName(ActiveProfileName);
        }

        return true;
    }

    public bool UpdateProfile(string profileName, ModList modList)
    {
        var profile = profiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
        if (profile == null)
            return false;

        var updatedProfile = CaptureModList(profileName, modList);
        profile.Description = updatedProfile.Description;
        profile.Mods = updatedProfile.Mods;
        ProfileStorage.Save(profile);
        return true;
    }

    public void MarkDiverged() => HasDiverged = true;

    public ProfileData GetStartupProfile()
    {
        return profiles.Count switch
        {
            0 => null,
            1 => profiles[0],
            _ => null,
        };
    }
    
    private static ProfileData CaptureModList(string profileName, ModList modList)
    {
        var profile = new ProfileData { Name = profileName };
        foreach (var mod in modList.AllMods)
        {
            profile.Mods.Add(new ProfileModEntry
            {
                DirectoryPath = mod.DirectoryPath ?? "",
                WorkshopHandle = mod.WorkshopHandle,
                Enabled = mod.Enabled,
            });
        }

        return profile;
    }
}