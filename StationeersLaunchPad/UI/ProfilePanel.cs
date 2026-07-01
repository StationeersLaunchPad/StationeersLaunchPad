using System.Linq;
using ImGuiNET;
using StationeersLaunchPad.Metadata;

namespace StationeersLaunchPad.UI;

public static class ProfilePanel
{
  private static string selectedName = "";
  private static string newProfileName = "";
  private static string message = "";
  private static bool selectActive;

  public static void SelectActive() => selectActive = true;

  public static bool Draw(ProfileManager manager, ModList modList)
  {
    manager.Initialize();
    if (selectActive)
    {
      selectedName = manager.ActiveProfileName;
      selectActive = false;
    }
    if (!string.IsNullOrEmpty(selectedName) && manager.FindProfile(selectedName) == null)
      selectedName = "";

    var changed = false;
    var activeName = string.IsNullOrEmpty(manager.ActiveProfileName)
      ? "Off (normal mod configuration)"
      : manager.ActiveProfileName;
    ImGuiHelper.Text($"Active: {activeName}");
    ImGui.Spacing();

    var selected = manager.FindProfile(selectedName);
    ImGui.SetNextItemWidth(-float.Epsilon);
    if (ImGui.BeginCombo("##profiles", selected?.Name ?? "Off (normal mod configuration)"))
    {
      if (ImGui.Selectable("Off (normal mod configuration)", selected == null))
        selectedName = "";
      foreach (var profile in manager.AllProfiles)
      {
        if (ImGui.Selectable(profile.Name, profile == selected))
          selectedName = profile.Name;
      }
      ImGui.EndCombo();
    }

    selected = manager.FindProfile(selectedName);
    if (selected == null)
    {
      ImGui.BeginDisabled(string.IsNullOrEmpty(manager.ActiveProfileName));
      if (ImGui.Button("Use Normal Mod Configuration"))
      {
        manager.DisableProfiles();
        message = "Profiles are off";
      }
      ImGui.EndDisabled();
      ImGuiHelper.TextDisabled("LaunchPad will keep using its normal mod configuration.");
    }
    else
    {
      if (ImGui.Button("Use Profile"))
      {
        changed = manager.ApplyProfile(selected.Name, modList);
        message = changed ? $"Loaded {selected.Name}" : "Profile could not be loaded";
      }
      ImGui.SameLine();
      if (ImGui.Button("Update from Current Mods"))
        message = manager.UpdateProfile(selected.Name, modList)
          ? $"Updated {selected.Name}"
          : "Profile could not be updated";
      ImGui.SameLine();
      if (ImGui.Button("Delete"))
      {
        message = manager.DeleteProfile(selected.Name)
          ? $"Deleted {selected.Name}"
          : "Profile could not be deleted";
        selectedName = "";
      }

      var enabled = selected.Mods.Count(mod => mod.Enabled);
      ImGuiHelper.TextDisabled($"{enabled} enabled, {selected.Mods.Count} installed when last updated");
      if (manager.HasDiverged(selected.Name, modList))
        ImGuiHelper.TextWarning("The current enabled mods or load order differ from this profile.");

      var newMods = manager.GetNewMods(selected.Name, modList);
      if (newMods.Count > 0)
      {
        ImGui.Separator();
        ImGuiHelper.TextWarning($"{newMods.Count} new mod{(newMods.Count == 1 ? " is" : "s are")} not in this profile:");
        foreach (var mod in newMods.Take(8))
          ImGui.BulletText($"{mod.Name} ({mod.Source})");
        if (newMods.Count > 8)
          ImGuiHelper.TextDisabled($"...and {newMods.Count - 8} more");

        if (ImGui.Button("Add to Profile"))
        {
          var updated = manager.UpdateProfile(selected.Name, modList);
          changed = updated && manager.ApplyProfile(selected.Name, modList);
          message = changed ? $"Added new mods to {selected.Name}" : "Profile could not be updated";
        }
        ImGui.SameLine();
        if (ImGui.Button("Keep Profile Unchanged"))
        {
          changed = manager.ApplyProfile(selected.Name, modList);
          message = changed ? $"Loaded {selected.Name} without the new mods" : "Profile could not be loaded";
        }
      }
    }

    ImGui.Separator();
    ImGui.SetNextItemWidth(-120f);
    var create = ImGui.InputText("##newprofile", ref newProfileName, 80, ImGuiInputTextFlags.EnterReturnsTrue);
    ImGui.SameLine();
    create |= ImGui.Button("Create Profile");
    if (create)
    {
      if (manager.CreateProfile(newProfileName, modList))
      {
        selectedName = newProfileName;
        manager.ApplyProfile(selectedName, modList);
        message = $"Created {selectedName}";
        newProfileName = "";
      }
      else
        message = "Use a unique name without file name characters";
    }

    if (!string.IsNullOrEmpty(message))
      ImGuiHelper.TextDisabled(message);
    return changed;
  }
}
