using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ImGuiNET;
using StationeersLaunchPad.Metadata;
using UI.ImGuiUi;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public enum ProfileStartupResult
{
    Chosen,
    Skipped,
    ManageProfiles,
}

public class ProfileStartupDialog
{
    private static int selectedIndex = 0;
    private static string chosenName = null;
    private static List<ProfileData> profiles;
    private static ProfileStartupResult result;
    
    public static bool IsActive { get; private set; }

    public static async UniTask<(ProfileStartupResult result, string name)> Show(IReadOnlyList<ProfileData> profileList)
    {
        selectedIndex = 0;
        chosenName = null;
        profiles = new List<ProfileData>(profileList);
        result = ProfileStartupResult.Skipped;
        IsActive = true;

        while (IsActive)
            await UniTask.Yield();

        return (result, chosenName);
    }

    public static void Draw()
    {
        if (!IsActive)
            return;
        
        ImGuiHelper.Draw(DrawDialog);
    }

    private static void DrawDialog()
    {
        var size = new Vector2(620, 400);
        var center = ImguiHelper.ScreenCenter;
        ImGui.SetNextWindowSize(size);
        ImGui.SetNextWindowPos(center - size / 2);
        ImGui.SetNextWindowFocus();
        ImGui.Begin(
            "Select Startup Profile##profilestartup",
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings
        );
        
        ImGuiHelper.TextWrapped("Multiple mod profiles found. Select a profile to load, or manage profiles manually.");
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var spacing = ImGui.GetStyle().ItemSpacing;
        var available = ImGui.GetContentRegionAvail();
        var buttonRowHeight = 35f + spacing.y * 2 + 1f;
        var listHeight = available.y - buttonRowHeight;

        ImGui.BeginChild("##profilestatuplist", new Vector2(available.x, listHeight));
        for (var i = 0; i < profiles.Count; i++)
        {
            ImGui.PushID(i);
            if (ImGui.Selectable(profiles[i].Name, i == selectedIndex))
                selectedIndex = i;
            if (!string.IsNullOrWhiteSpace(profiles[i].Description))
                ImGuiHelper.ItemTooltip(profiles[i].Description);
            ImGui.PopID();
        }
        ImGui.EndChild();
        
        ImGui.Separator();
        ImGui.Spacing();

        var buttonWidth = (available.x - spacing.x * 2) / 3f;
        var buttonSize = new Vector2(buttonWidth, 35f);

        if (ImGui.Button("Load Selected", buttonSize))
        {
            chosenName = profiles[selectedIndex].Name;
            result = ProfileStartupResult.Chosen;
            IsActive = false;
        }
        
        ImGui.SameLine();

        if (ImGui.Button("Manage Profiles", buttonSize))
        {
            result = ProfileStartupResult.ManageProfiles;
            IsActive = false;
        }
        ImGuiHelper.ItemTooltip("Open the Profiles tab to create, edit, or delete profiles");
        
        ImGui.SameLine();

        if (ImGui.Button("Skip", buttonSize))
        {
            result = ProfileStartupResult.Skipped;
            IsActive = false;
        }

        ImGui.End();
    }
}