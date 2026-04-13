using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Assets.Scripts.Serialization;

namespace StationeersLaunchPad.Metadata;

[XmlRoot("ActiveProfile")]
public class ActiveProfileConfig
{
    [XmlAttribute("Name")]
    public string Name;
}

public class ProfileStorage
{
    public static string ProfilesDirectory => Path.Join(LaunchPadPaths.SavePath, "profiles");
    public static string ActiveConfigPath => Path.Join(LaunchPadPaths.SavePath, "active.xml");

    public static List<ProfileData> LoadAll()
    {
        var dir = ProfilesDirectory;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            return [];
        }

        var profiles = new List<ProfileData>();

        foreach (var file in Directory.GetFiles(dir, "*.xml"))
        {
            try
            {
                var profile = XmlSerialization.Deserialize<ProfileData>(file);
                if (profile != null)
                    profiles.Add(profile);
                else
                    Logger.Global.LogWarning($"Skipping corrupt profile file: {file}");
            }
            catch (Exception e)
            {
                Logger.Global.LogWarning($"Skipping corrupt profile file: {file} ({e.Message})");
            }
        }

        return profiles;
    }

    public static ProfileData Load(string profileName)
    {
        var path = GetProfilePath(profileName);
        if (!File.Exists(path))
            return null;

        try
        {
            return XmlSerialization.Deserialize<ProfileData>(path);
        }
        catch (Exception e)
        {
            Logger.Global.LogWarning($"Failed to load profile '{profileName}': {e.Message}");
            return null;
        }
    }

    public static void Save(ProfileData profile)
    {
        var dir = ProfilesDirectory;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var path = GetProfilePath(profile.Name);
        if (!profile.SaveXml(path))
            Logger.Global.LogError($"Failed to save profile to {path}");
    }

    public static void Delete(string profileName)
    {
        var path = GetProfilePath(profileName);
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception e)
        {
            Logger.Global.LogError($"Failed to delete profile '{profileName}': {e.Message}");
        }
    }

    public static ProfileData Rename(string oldProfileName, string newProfileName)
    {
        var profile = Load(oldProfileName);
        if (profile == null)
            return null;
        
        profile.Name = newProfileName;
        Save(profile);
        Delete(oldProfileName);
        return profile;
    }

    public static string LoadActiveName()
    {
        if (!File.Exists(ActiveConfigPath))
            return null;

        try
        {
            var config = XmlSerialization.Deserialize<ActiveProfileConfig>(ActiveConfigPath);
            return config?.Name;
        }
        catch (Exception e)
        {
            Logger.Global.LogError($"Failed to load active profile config: {e.Message}");
            return null;
        }
    }

    public static void SaveActiveName(string profileName)
    {
        var dir = ProfilesDirectory;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var config = new ActiveProfileConfig { Name = profileName };
        if (!config.SaveXml(ActiveConfigPath))
            Logger.Global.LogError($"Failed to save active profile config to {ActiveConfigPath}");
    }

    public static void ClearActiveName()
    {
        try
        {
            if (File.Exists(ActiveConfigPath))
                File.Delete(ActiveConfigPath);
        }
        catch (Exception e)
        {
            Logger.Global.LogError($"Failed to clear active profile config: {e.Message}");
        }
    }

    private static string GetProfilePath(string profileName)
    {
        var sanitized = Platform.MakeValidFileName(profileName).ToLowerInvariant();
        return Path.Join(ProfilesDirectory, $"{sanitized}.xml");
    }
}