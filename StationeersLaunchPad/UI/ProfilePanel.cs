using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assets.Scripts;
using Assets.Scripts.Networking.Transports;
using Cysharp.Threading.Tasks;
using ImGuiNET;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public static class ProfilePanel
{
  private static string selectedName = "";
  private static string newProfileName = "";
  private static string message = "";
  private static string confirmDelete = "";
  private static string confirmEmptySave = "";
  private static string packageCode = "";
  private static DateTime confirmationExpires;
  private static bool selectActive;
  private static bool importingPackage;
  private static readonly HashSet<ulong> subscriptions = [];

  public static bool Busy => subscriptions.Count > 0 || importingPackage;

  public static void SelectActive() => selectActive = true;

  public static bool Draw(LoadStage stage, ProfileManager manager, ModList modList)
  {
    if ((!string.IsNullOrEmpty(confirmDelete) || !string.IsNullOrEmpty(confirmEmptySave))
      && DateTime.UtcNow > confirmationExpires)
      ClearConfirmations();
    manager.Initialize();
    if (selectActive)
    {
      selectedName = manager.ActiveProfileName;
      selectActive = false;
    }
    else if (string.IsNullOrEmpty(selectedName) && !string.IsNullOrEmpty(manager.ActiveProfileName))
      selectedName = manager.ActiveProfileName;
    if (!string.IsNullOrEmpty(selectedName) && manager.FindProfile(selectedName) == null)
      selectedName = "";

    var changed = DrawProfilePicker(manager, modList);
    var selected = manager.FindProfile(selectedName);
    if (selected == null)
    {
      DrawOffState(manager);
      DrawWorkshopPackage(stage, manager, modList, null);
    }
    else
      changed |= DrawProfileActions(stage, manager, modList, selected);

    DrawCreateProfile(manager, modList, ref changed);
    DrawProfileContents(stage, manager, selected, modList);
    return changed;
  }

  private static bool DrawProfilePicker(ProfileManager manager, ModList modList)
  {
    var changed = false;
    ImGuiHelper.Text("Profile");
    ImGui.SameLine();
    if (string.IsNullOrEmpty(manager.ActiveProfileName))
      ImGuiHelper.TextDisabled("Off");
    else
      ImGuiHelper.TextColored($"Selected: {manager.ActiveProfileName}", LaunchPadTheme.Accent);

    var selected = manager.FindProfile(selectedName);
    ImGui.SetNextItemWidth(-float.Epsilon);
    if (!ImGui.BeginCombo("##profiles", selected?.Name ?? "Off (normal mod configuration)"))
      return false;

    if (ImGui.Selectable("Off (normal mod configuration)", selected == null))
    {
      selectedName = "";
      packageCode = "";
      ClearConfirmations();
      manager.DisableProfiles();
      message = "Profiles are off. The current mod list is unchanged.";
    }
    foreach (var profile in manager.AllProfiles)
    {
      if (!ImGui.Selectable(profile.Name, profile == selected))
        continue;

      selectedName = profile.Name;
      packageCode = "";
      ClearConfirmations();
      changed |= manager.ApplyProfile(profile.Name, modList);
      message = changed ? $"Switched to {profile.Name}" : "Profile could not be loaded";
    }
    ImGui.EndCombo();
    return changed;
  }

  private static void DrawOffState(ProfileManager manager)
  {
    ImGuiHelper.TextWrapped("Profiles are optional. Create one from the mod list on the left, or select an existing profile above.");
    if (!string.IsNullOrEmpty(manager.ActiveProfileName) && ImGui.Button("Turn Profiles Off"))
      manager.DisableProfiles();
    DrawMessage();
  }

  private static bool DrawProfileActions(
    LoadStage stage, ProfileManager manager, ModList modList, ProfileData selected)
  {
    var changed = false;
    var diverged = manager.HasDiverged(selected.Name, modList);
    if (diverged)
    {
      ImGuiHelper.TextWarning("The current mod list differs from this saved profile.");
      ImGuiHelper.TextDisabled("Save the current list, or apply the saved profile to discard these changes.");
    }
    else
      ImGuiHelper.TextDisabled("The mod list on the left matches this profile.");

    ImGui.BeginDisabled(!diverged);
    PushPrimaryButton();
    var emptySave = modList.EnabledMods.All(mod => mod.Source == ModSourceType.Core)
      && selected.Mods.Any(entry => entry.Enabled && !IsCore(entry));
    if (!emptySave)
      confirmEmptySave = "";
    var saveText = emptySave && confirmEmptySave == selected.Name
      ? "Confirm Empty Profile"
      : "Save Current List";
    if (ImGui.Button(saveText))
    {
      if (emptySave && confirmEmptySave != selected.Name)
      {
        confirmEmptySave = selected.Name;
        confirmationExpires = DateTime.UtcNow.AddSeconds(5);
        message = $"Click again to save {selected.Name} with no enabled mods";
      }
      else
      {
        message = manager.UpdateProfile(selected.Name, modList)
          ? $"Saved current mod list to {selected.Name}"
          : "Profile could not be updated";
        confirmEmptySave = "";
      }
    }
    ImGui.PopStyleColor(3);
    ImGui.EndDisabled();

    ImGui.SameLine();
    if (ImGui.Button("Apply Saved Profile"))
    {
      ClearConfirmations();
      changed |= manager.ApplyProfile(selected.Name, modList);
      message = changed ? $"Applied {selected.Name}" : "Profile could not be loaded";
    }
    ImGui.SameLine();
    var deleteText = confirmDelete == selected.Name ? "Confirm Delete" : "Delete Profile";
    if (ImGui.Button(deleteText))
    {
      if (confirmDelete != selected.Name)
      {
        confirmDelete = selected.Name;
        confirmationExpires = DateTime.UtcNow.AddSeconds(5);
        message = $"Click again to delete {selected.Name}";
      }
      else
      {
        message = manager.DeleteProfile(selected.Name)
          ? $"Deleted {selected.Name}"
          : "Profile could not be deleted";
        selectedName = "";
        ClearConfirmations();
      }
    }

    var newMods = manager.GetNewMods(selected.Name, modList);
    if (newMods.Count > 0)
    {
      ImGui.Spacing();
      ImGuiHelper.TextWarning($"{newMods.Count} new mod{(newMods.Count == 1 ? " is" : "s are")} not in this profile:");
      foreach (var mod in newMods.Take(5))
        ImGui.BulletText($"{mod.Name} ({mod.Source})");
      if (newMods.Count > 5)
        ImGuiHelper.TextDisabled($"...and {newMods.Count - 5} more");

      if (ImGui.Button("Add to Profile"))
      {
        var updated = manager.UpdateProfile(selected.Name, modList);
        changed |= updated && manager.ApplyProfile(selected.Name, modList);
        message = changed ? $"Added new mods to {selected.Name}" : "Profile could not be updated";
      }
      ImGui.SameLine();
      if (ImGui.Button("Keep Profile Unchanged"))
      {
        changed |= manager.ApplyProfile(selected.Name, modList);
        message = changed ? $"Loaded {selected.Name} without the new mods" : "Profile could not be loaded";
      }
    }

    DrawWorkshopPackage(stage, manager, modList, selected);
    DrawMessage();
    return changed;
  }

  private static void DrawWorkshopPackage(
    LoadStage stage, ProfileManager manager, ModList modList, ProfileData profile)
  {
    var entries = profile?.Mods
      .Where(entry => entry.Enabled && !IsCore(entry))
      .ToList() ?? [];
    var canCopy = entries.Count > 0 && entries.All(entry =>
      entry.Source == ModSourceType.Workshop && entry.WorkshopHandle > 1);

    ImGui.Spacing();
    ImGuiHelper.Text("Workshop package");
    ImGuiHelper.TextDisabled(canCopy
      ? "Share this profile's Workshop items and load order as a package code."
      : "Import a Workshop package into the current mod list.");
    var import = ImGui.InputTextWithHint(
      "##packagecode", "SLP1 package code", ref packageCode, 16384,
      ImGuiInputTextFlags.EnterReturnsTrue);

    if (canCopy && ImGui.Button("Copy Code"))
    {
      packageCode = WorkshopPackageCode.Encode(entries.Select(entry => entry.WorkshopHandle));
      GameManager.Clipboard = packageCode;
      message = "Workshop package code copied";
    }
    if (canCopy)
      ImGui.SameLine();
    var canImport = stage == LoadStage.Configuring
      && !Busy && !string.IsNullOrWhiteSpace(packageCode);
    ImGui.BeginDisabled(!canImport);
    import = ImGui.Button(importingPackage ? "Importing..." : "Import Code")
      || import && canImport;
    if (import)
    {
      if (!WorkshopPackageCode.TryDecode(packageCode, out var workshopIds))
        message = "That Workshop package code is invalid";
      else
        ImportPackage(manager, modList, workshopIds).Forget();
    }
    ImGui.EndDisabled();
    ImGuiHelper.ItemTooltip(
      stage == LoadStage.Configuring
        ? "Replace the current enabled mods with this Workshop package"
        : "Packages can only be imported before loading mods",
      hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled);
    ImGuiHelper.TextDisabled("Import changes the current mod list. Save it to the profile when ready.");
  }

  private static void DrawCreateProfile(ProfileManager manager, ModList modList, ref bool changed)
  {
    ImGui.Separator();
    ImGuiHelper.Text("Create from the current mod list");

    const string buttonText = "Create Profile";
    var style = ImGui.GetStyle();
    var buttonWidth = ImGui.CalcTextSize(buttonText).x + style.FramePadding.x * 2;
    var inputWidth = Math.Max(80f, ImGui.GetContentRegionAvail().x - buttonWidth - style.ItemSpacing.x);
    ImGui.SetNextItemWidth(inputWidth);
    var create = ImGui.InputTextWithHint(
      "##newprofile", "Profile name", ref newProfileName, 80,
      ImGuiInputTextFlags.EnterReturnsTrue);
    ImGui.SameLine();
    create |= ImGui.Button(buttonText);
    if (!create)
      return;

    if (manager.CreateProfile(newProfileName, modList))
    {
      selectedName = newProfileName;
      ClearConfirmations();
      changed |= manager.ApplyProfile(selectedName, modList);
      message = $"Created {selectedName}";
      newProfileName = "";
    }
    else
      message = "Use a unique name without file name characters";
  }

  private static void DrawProfileContents(
    LoadStage stage, ProfileManager manager, ProfileData selected, ModList modList)
  {
    ImGui.Separator();
    if (selected == null)
    {
      ImGuiHelper.TextDisabled("No profile selected.");
      return;
    }

    var entries = selected.Mods
      .Where(entry => entry.Enabled && !IsCore(entry))
      .ToList();
    var currentCount = modList.EnabledMods.Count(mod => mod.Source != ModSourceType.Core);
    ImGuiHelper.Text($"Saved mods in {selected.Name}");
    ImGui.SameLine();
    ImGuiHelper.TextDisabled($"{entries.Count} saved, {currentCount} currently enabled");
    ImGuiHelper.TextDisabled("Edit using the mod list on the left, then save the changes here.");

    ImGui.PushStyleColor(ImGuiCol.ChildBg, (Vector4)LaunchPadTheme.PanelAlt);
    ImGui.PushStyleColor(ImGuiCol.Border, (Vector4)LaunchPadTheme.Border);
    ImGui.BeginChild("##profilecontents", new Vector2(0, 0), true);
    for (var i = 0; i < entries.Count; i++)
      DrawProfileMod(stage, manager, selected, entries[i], i, modList);
    if (entries.Count == 0)
      ImGuiHelper.TextDisabled("This profile has no enabled mods.");

    var unavailable = selected.Mods
      .Where(entry => !entry.Enabled && !IsCore(entry)
        && ProfileManager.FindMod(entry, modList.AllMods) == null)
      .ToList();
    if (unavailable.Count > 0)
    {
      ImGui.Separator();
      ImGuiHelper.Text("Unavailable saved mods");
      foreach (var (entry, index) in unavailable.Select((entry, index) => (entry, index)))
        DrawProfileMod(stage, manager, selected, entry, entries.Count + index, modList);
    }
    ImGui.EndChild();
    ImGui.PopStyleColor(2);
  }

  private static void DrawProfileMod(
    LoadStage stage, ProfileManager manager, ProfileData profile,
    ProfileModEntry entry, int index, ModList modList)
  {
    var mod = ProfileManager.FindMod(entry, modList.AllMods);
    var source = mod?.Source.ToString() ?? (entry.Source == ModSourceType.Core ? "Mod" : entry.Source.ToString());
    var name = mod?.Name ?? GetFallbackName(entry);
    if (mod != null && modList.IsBetaMod(mod))
      name += " [BETA]";

    ImGui.PushID(index);
    ImGuiHelper.TextDisabled($"{index + 1}.");
    ImGui.SameLine();
    ImGuiHelper.TextColored(source, LaunchPadTheme.TextMuted);
    ImGui.SameLine();
    ImGuiHelper.Text(name);
    if (mod == null)
    {
      ImGui.SameLine();
      ImGuiHelper.TextWarning("Not installed");
      if (entry.WorkshopHandle > 1)
      {
        ImGui.SameLine();
        var busy = subscriptions.Contains(entry.WorkshopHandle);
        ImGui.BeginDisabled(stage != LoadStage.Configuring || busy);
        if (ImGui.Button(busy ? "Subscribing..." : "Subscribe"))
          Subscribe(entry).Forget();
        ImGui.EndDisabled();
        ImGuiHelper.ItemTooltip(
          stage == LoadStage.Configuring
            ? $"Subscribe to Workshop item {entry.WorkshopHandle}"
            : "Workshop items can only be subscribed before loading mods",
          hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled);
      }
      ImGui.SameLine();
      if (ImGui.Button("Remove from Profile"))
        message = manager.RemoveMod(profile.Name, entry)
          ? $"Removed {name} from {profile.Name}"
          : $"Could not remove {name}";
    }
    else if (!mod.Enabled)
    {
      ImGui.SameLine();
      ImGuiHelper.TextWarning("Pending removal");
    }
    ImGui.PopID();
  }

  private static async UniTask Subscribe(ProfileModEntry entry)
  {
    if (!subscriptions.Add(entry.WorkshopHandle))
      return;

    message = $"Subscribing to Workshop item {entry.WorkshopHandle}...";
    try
    {
      if (!await Steam.SubscribeAndDownload(entry.WorkshopHandle))
      {
        message = $"Could not subscribe to Workshop item {entry.WorkshopHandle}";
        return;
      }

      var config = ModConfigUtil.LoadConfig();
      var workshop = config.Mods
        .OfType<WorkshopModData>()
        .FirstOrDefault(mod => (ulong)mod.WorkshopId == entry.WorkshopHandle);
      if (workshop == null)
      {
        workshop = new();
        config.Mods.Add(workshop);
      }
      workshop.Enabled = entry.Enabled;
      workshop.DirectoryPath = new(entry.DirectoryPath);
      workshop.WorkshopId = new(entry.WorkshopHandle);
      ModConfigUtil.SaveConfig(config);

      message = $"Subscribed to Workshop item {entry.WorkshopHandle}";
      LaunchPadConfig.ReloadMods();
    }
    finally
    {
      subscriptions.Remove(entry.WorkshopHandle);
    }
  }

  private static async UniTask ImportPackage(
    ProfileManager manager, ModList modList, List<ulong> workshopIds)
  {
    if (importingPackage)
      return;

    importingPackage = true;
    message = $"Importing {workshopIds.Count} Workshop mods...";
    try
    {
      var installed = modList.AllMods
        .Where(mod => mod.Source == ModSourceType.Workshop)
        .Select(mod => mod.WorkshopHandle)
        .ToHashSet();
      foreach (var workshopId in workshopIds.Where(id => !installed.Contains(id)))
      {
        if (!await Steam.SubscribeAndDownload(workshopId))
        {
          message = $"Could not install Workshop item {workshopId}";
          return;
        }
      }

      var config = modList.ToModConfig();
      var original = config.Mods.ToList();
      var reordered = original.Where(mod => mod is CoreModData).ToList();
      var workshopBase = SteamTransport.WorkshopType.Mod.GetLocalDirInfo().FullName;
      foreach (var workshopId in workshopIds)
      {
        var workshop = original
          .OfType<WorkshopModData>()
          .FirstOrDefault(mod => (ulong)mod.WorkshopId == workshopId);
        if (workshop == null)
        {
          workshop = new()
          {
            DirectoryPath = new(Path.Combine(workshopBase, workshopId.ToString())),
            WorkshopId = new(workshopId),
          };
        }
        workshop.Enabled = true;
        reordered.Add(workshop);
      }

      foreach (var mod in original.Where(mod => !reordered.Contains(mod)))
      {
        mod.Enabled = false;
        reordered.Add(mod);
      }
      config.Mods.Clear();
      config.Mods.AddRange(reordered);
      manager.MarkActiveProfileDirty();
      ModConfigUtil.SaveConfig(config);

      message = $"Imported {workshopIds.Count} Workshop mods";
      LaunchPadConfig.ReloadMods();
    }
    catch (Exception ex)
    {
      Logger.Global.LogException(ex);
      message = "Workshop package could not be imported";
    }
    finally
    {
      importingPackage = false;
    }
  }

  private static string GetFallbackName(ProfileModEntry entry)
  {
    if (!string.IsNullOrEmpty(entry.Name))
      return entry.Name;
    if (!string.IsNullOrEmpty(entry.ModID))
      return entry.ModID;
    if (entry.WorkshopHandle > 1)
      return $"Workshop {entry.WorkshopHandle}";
    var name = Path.GetFileName(entry.DirectoryPath?.TrimEnd('/', '\\'));
    return string.IsNullOrEmpty(name) ? "Unknown mod" : name;
  }

  private static bool IsCore(ProfileModEntry entry) =>
    string.IsNullOrEmpty(entry.DirectoryPath) && entry.WorkshopHandle <= 1;

  private static void DrawMessage()
  {
    if (!string.IsNullOrEmpty(message))
      ImGuiHelper.TextDisabled(message);
  }

  private static void PushPrimaryButton()
  {
    ImGui.PushStyleColor(ImGuiCol.Button, (Vector4)LaunchPadTheme.AccentSoft);
    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, (Vector4)LaunchPadTheme.AccentStrong);
    ImGui.PushStyleColor(ImGuiCol.ButtonActive, (Vector4)LaunchPadTheme.AccentBorder);
  }

  private static void ClearConfirmations()
  {
    confirmDelete = "";
    confirmEmptySave = "";
    confirmationExpires = DateTime.MinValue;
  }
}
