using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assets.Scripts.Serialization;

namespace StationeersLaunchPad.Metadata;

public static class ProfileStorage
{
  public static string ProfilesDirectory => Path.Join(LaunchPadPaths.SavePath, "profiles");

  public static List<ProfileData> LoadAll()
  {
    if (!Directory.Exists(ProfilesDirectory))
      return [];

    var profiles = new List<ProfileData>();
    foreach (var file in Directory.GetFiles(ProfilesDirectory, "*.xml").OrderBy(path => path))
    {
      try
      {
        var profile = XmlSerialization.Deserialize<ProfileData>(file);
        if (profile != null && IsValidName(profile.Name))
        {
          profile.Mods = profile.Mods.Where(entry => entry.LegacyEnabled).ToList();
          profiles.Add(profile);
        }
        else
          Logger.Global.LogWarning($"Skipping invalid profile file: {file}");
      }
      catch (Exception ex)
      {
        Logger.Global.LogWarning($"Skipping invalid profile file: {file} ({ex.Message})");
      }
    }
    return profiles;
  }

  public static bool Save(ProfileData profile)
  {
    if (!IsValidName(profile?.Name))
      return false;

    if (!Directory.Exists(ProfilesDirectory))
      Directory.CreateDirectory(ProfilesDirectory);

    var path = GetProfilePath(profile.Name);
    if (profile.SaveXml(path))
      return true;

    Logger.Global.LogError($"Failed to save profile to {path}");
    return false;
  }

  public static bool Delete(string profileName)
  {
    if (!IsValidName(profileName))
      return false;

    try
    {
      var path = GetProfilePath(profileName);
      if (File.Exists(path))
        File.Delete(path);
      return true;
    }
    catch (Exception ex)
    {
      Logger.Global.LogError($"Failed to delete profile '{profileName}': {ex.Message}");
      return false;
    }
  }

  public static bool IsValidName(string profileName)
  {
    if (string.IsNullOrWhiteSpace(profileName))
      return false;

    var name = profileName.Trim();
    return name == profileName
      && name is not "." and not ".."
      && Platform.MakeValidFileName(name) == name;
  }

  private static string GetProfilePath(string profileName) =>
    Path.Join(ProfilesDirectory, $"{profileName.ToLowerInvariant()}.xml");
}
