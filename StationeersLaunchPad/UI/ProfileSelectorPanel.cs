using Cysharp.Threading.Tasks;
using ImGuiNET;
using StationeersLaunchPad.Metadata;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public class ProfileSelectorPanel
{
    private static int selectedIndex = -1;
    private static string pendingName = "";
    private static bool showCreateInput = false;
    private static bool showRenameInput = false;

    public static ManualLoadWindow.ChangeFlags Draw(ProfileManager profileManager, ModList modList, LoadStage stage)
    {
        var changed = ManualLoadWindow.ChangeFlags.None;
        var disabled = stage != LoadStage.Configuring;

        ImGui.BeginDisabled(disabled);

        var profiles = profileManager.AllProfiles;

        if (selectedIndex >= profiles.Count)
            selectedIndex = profiles.Count - 1;

        var listHeight = ImGui.GetContentRegionAvail().y - ImGui.GetTextLineHeightWithSpacing() * 4f;
        ImGui.BeginChild("##profilelist", new Vector2(0, listHeight));
        for (var i = 0; i < profiles.Count; i++)
        {
            ImGui.PushID(i);
            var profile = profiles[i];
            var isActive = profile.Name == profileManager.ActiveProfileName;
            var isSelected = i == selectedIndex;

            if (isActive)
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 1f, 0.4f, 1f));

            var label = isActive
                ? $"{profile.Name}{(profileManager.HasDiverged ? " *" : " [active]")}"
                : profile.Name;

            if (ImGui.Selectable(label, isSelected))
                selectedIndex = i;

            if (isActive)
                ImGui.PopStyleColor();

            ImGuiHelper.ItemTooltip(isActive
                ? (profileManager.HasDiverged ? "Active profile (modified)" : "Active profile")
                : profile.Name);

            ImGui.PopID();
        }
        ImGui.EndChild();

        ImGui.Separator();

        var hasSelection = selectedIndex >= 0 && selectedIndex < profiles.Count;
        var selectedProfile = hasSelection ? profiles[selectedIndex] : null;

        if (ImGui.Button("Create"))
        {
            pendingName = "";
            showCreateInput = true;
            showRenameInput = false;
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(!hasSelection);

        if (ImGui.Button("Load"))
        {
            if (profileManager.LoadProfile(selectedProfile!.Name, modList))
                changed |= ManualLoadWindow.ChangeFlags.Mods;
        }
        ImGuiHelper.ItemTooltip("Apply this profile to the current mod list");

        ImGui.SameLine();
        if (ImGui.Button("Update"))
        {
            profileManager.UpdateProfile(selectedProfile!.Name, modList);
        }
        ImGuiHelper.ItemTooltip("Overwrite this profile with the current mod list");

        ImGui.SameLine();
        if (ImGui.Button("Rename"))
        {
            pendingName = selectedProfile!.Name;
            showRenameInput = true;
            showCreateInput = false;
        }

        ImGui.SameLine();
        if (ImGui.Button("Delete"))
        {
            ConfirmDelete(profileManager, selectedProfile!.Name).Forget();
        }

        ImGui.EndDisabled();

        if (showCreateInput)
        {
            ImGui.Separator();
            ImGui.SetNextItemWidth(200f);
            ImGui.InputText("##createname", ref pendingName, 128);
            ImGui.SameLine();
            if (ImGui.Button("OK##create"))
            {
                if (profileManager.CreateProfile(pendingName, modList))
                {
                    selectedIndex = profileManager.AllProfiles.Count - 1;
                    showCreateInput = false;
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel##create"))
                showCreateInput = false;
        }

        if (showRenameInput && hasSelection)
        {
            ImGui.Separator();
            ImGui.SetNextItemWidth(200f);
            ImGui.InputText("##renameinput", ref pendingName, 128);
            ImGui.SameLine();
            if (ImGui.Button("OK##rename"))
            {
                if (profileManager.RenameProfile(selectedProfile!.Name, pendingName))
                    showRenameInput = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel##rename"))
                showRenameInput = false;
        }

        ImGui.EndDisabled();

        return changed;
    }

    private static async UniTaskVoid ConfirmDelete(ProfileManager profileManager, string name)
    {
        var confirmed = false;
        await AlertPopup.Show(
            "Delete Profile",
            $"Delete profile '{name}'? This cannot be undone.",
            AlertPopup.DefaultSize,
            AlertPopup.DefaultPosition,
            ("Delete", () => { confirmed = true; return true; }),
            ("Cancel", () => true)
        );
        if (confirmed)
        {
            profileManager.DeleteProfile(name);
            selectedIndex = -1;
        }
    }
}