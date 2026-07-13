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
  private static ModList indexedModList;
  private static IReadOnlyDictionary<string, ModInfo> modIndex;

  public static bool Busy => subscriptions.Count > 0 || importingPackage;
  public static string BusyText => importingPackage ? "Importing SLP1..." : "Subscribing...";

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
    if (!ReferenceEquals(indexedModList, modList))
    {
      indexedModList = modList;
      modIndex = ProfileManager.BuildModIndex(modList.AllMods);
    }

    var changed = false;
    if (!ImGui.BeginTabBar("##profileviews"))
      return false;

    if (ImGui.BeginTabItem("Profile"))
    {
      changed |= DrawProfileManagement(stage, manager, modList, modIndex);
      ImGui.EndTabItem();
    }
    if (ImGui.BeginTabItem("Share Profile"))
    {
      var selected = manager.FindProfile(selectedName);
      if (selected == null)
        ImGuiHelper.TextDisabled("No profile selected. You can still load a shared SLP1 code.");
      else
        ImGuiHelper.TextColored($"Selected: {selected.Name}", LaunchPadTheme.Accent);
      DrawWorkshopPackage(stage, manager, modList, selected, modIndex);
      DrawMessage();
      ImGui.EndTabItem();
    }
    ImGui.EndTabBar();
    return changed;
  }

  private static bool DrawProfileManagement(
    LoadStage stage, ProfileManager manager, ModList modList,
    IReadOnlyDictionary<string, ModInfo> modIndex)
  {
    var changed = DrawProfilePicker(manager, modList);
    var selected = manager.FindProfile(selectedName);
    if (selected == null)
      DrawOffState(manager);
    else
      changed |= DrawProfileActions(stage, manager, modList, selected, modIndex);

    DrawCreateProfile(manager, modList, ref changed);
    DrawProfileContents(stage, manager, selected, modList, modIndex);
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
      if (profile == selected)
        continue;

      var active = manager.ActiveProfile;
      if (active != null && (manager.HasDiverged(active.Name, modList)
        || manager.GetNewMods(active.Name, modList).Count > 0))
      {
        message = $"Save or revert changes to {active.Name} before switching profiles";
        continue;
      }

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
    LoadStage stage, ProfileManager manager, ModList modList, ProfileData selected,
    IReadOnlyDictionary<string, ModInfo> modIndex)
  {
    var changed = false;
    var diverged = manager.HasDiverged(selected.Name, modList, modIndex);
    var newMods = manager.GetNewMods(selected.Name, modList);
    var hasPendingChanges = diverged || newMods.Count > 0;
    if (diverged)
    {
      ImGuiHelper.TextWarning($"Unsaved changes to {selected.Name}");
      ImGuiHelper.TextDisabled("The mod list on the left is a working copy. Save it, or revert to the saved profile.");
    }
    else if (newMods.Count > 0)
    {
      ImGuiHelper.TextWarning($"{newMods.Count} new mod{(newMods.Count == 1 ? " needs" : "s need")} a profile decision");
      ImGuiHelper.TextDisabled("Save Changes to include their current state, or keep the saved profile.");
    }
    else
    {
      ImGuiHelper.TextSuccess($"{selected.Name} matches the mod list on the left.");
      ImGuiHelper.TextDisabled("Changes made on the left are not saved to this profile automatically.");
    }

    ImGui.BeginDisabled(!hasPendingChanges);
    PushPrimaryButton();
    var emptySave = modList.EnabledMods.All(mod => mod.Source == ModSourceType.Core)
      && selected.Mods.Any(entry => entry.Enabled && !IsCore(entry));
    if (!emptySave)
      confirmEmptySave = "";
    var saveText = emptySave && confirmEmptySave == selected.Name
      ? "Confirm Save Empty"
      : "Save Changes";
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
          ? $"Saved changes to {selected.Name}"
          : "Profile could not be updated";
        confirmEmptySave = "";
      }
    }
    ImGui.PopStyleColor(3);
    ImGui.EndDisabled();
    ImGuiHelper.ItemTooltip(
      $"Update {selected.Name} with the mod states and load order shown on the left.",
      hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled);

    ImGui.SameLine();
    ImGui.BeginDisabled(!hasPendingChanges);
    var revertText = newMods.Count > 0 ? "Keep Saved Profile" : "Revert Changes";
    if (ImGui.Button(revertText))
    {
      ClearConfirmations();
      var applied = manager.ApplyProfile(selected.Name, modList);
      changed |= applied;
      var remembered = applied
        && (newMods.Count == 0 || manager.UpdateProfile(selected.Name, modList));
      message = !applied
        ? "Profile could not be loaded"
        : !remembered
          ? "The new mod decision could not be saved"
          : newMods.Count > 0
            ? $"Kept {selected.Name} unchanged and disabled new mods"
            : $"Reverted to the saved {selected.Name} profile";
    }
    ImGui.EndDisabled();
    ImGuiHelper.ItemTooltip(
      newMods.Count > 0
        ? $"Keep the saved {selected.Name} mods and remember new mods as disabled."
        : $"Discard pending changes and restore the saved {selected.Name} profile.",
      hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled);
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

    if (newMods.Count > 0)
    {
      ImGui.Spacing();
      ImGuiHelper.TextWarning($"{newMods.Count} new mod{(newMods.Count == 1 ? " is" : "s are")} not in this profile:");
      foreach (var mod in newMods.Take(5))
        ImGui.BulletText($"{mod.Name} ({mod.Source})");
      if (newMods.Count > 5)
        ImGuiHelper.TextDisabled($"...and {newMods.Count - 5} more");

      ImGuiHelper.TextDisabled("Save Changes includes them; Keep Saved Profile remembers them as disabled.");
    }

    DrawMessage();
    return changed;
  }

  private static void DrawWorkshopPackage(
    LoadStage stage, ProfileManager manager, ModList modList, ProfileData profile,
    IReadOnlyDictionary<string, ModInfo> modIndex)
  {
    var entries = profile?.Mods
      .Where(entry => entry.Enabled && !IsCore(entry))
      .ToList() ?? [];
    var savedCanCopy = entries.Count > 0 && entries.All(entry =>
      entry.Source == ModSourceType.Workshop && entry.WorkshopHandle > 1);
    var currentMods = modList.EnabledMods
      .Where(mod => mod.Source != ModSourceType.Core)
      .ToList();
    var currentCanCopy = currentMods.Count > 0 && currentMods.All(mod =>
      mod.Source == ModSourceType.Workshop && mod.WorkshopHandle > 1);
    var hasPendingChanges = profile != null
      && (manager.HasDiverged(profile.Name, modList, modIndex)
        || manager.GetNewMods(profile.Name, modList).Count > 0);
    var canCopy = savedCanCopy && !hasPendingChanges;
    var showCopy = canCopy || currentCanCopy;
    var copyRequirement = profile == null
      ? "Create a profile from the current selection on the Profile tab before copying an SLP1 code."
      : hasPendingChanges
        ? "Use Save Changes on the Profile tab before copying this SLP1 code."
        : "Remove non-Workshop entries from the saved profile before copying an SLP1 code.";

    ImGuiHelper.Text("Shareable SLP1 codes");
    ImGuiHelper.TextDisabled(savedCanCopy
      ? "Copy this Workshop-only profile's mod IDs and load order."
      : profile != null && entries.Count > 0
        ? "Only Workshop-only profiles can be copied. You can still load an SLP1 code below."
        : "SLP1 codes share Workshop mod IDs and load order. Paste one below to load it.");
    if (showCopy && !canCopy)
      ImGuiHelper.TextWarning(copyRequirement);
    ImGuiHelper.TextDisabled("They contain IDs and order, not mod files; missing Workshop items are downloaded.");
    var import = ImGui.InputTextWithHint(
      "##packagecode", "Paste an SLP1 code", ref packageCode, 16384,
      ImGuiInputTextFlags.EnterReturnsTrue);

    if (showCopy)
    {
      ImGui.BeginDisabled(!canCopy);
      if (ImGui.Button("Copy SLP1 Code"))
      {
        packageCode = WorkshopPackageCode.Encode(entries.Select(entry => entry.WorkshopHandle));
        GameManager.Clipboard = packageCode;
        message = "Shareable SLP1 code copied";
      }
      ImGui.EndDisabled();
      ImGuiHelper.ItemTooltip(
        canCopy ? "Copy the saved profile as an SLP1 code" : copyRequirement,
        hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled);
      ImGui.SameLine();
    }
    var canImport = stage == LoadStage.Configuring
      && !Busy && !string.IsNullOrWhiteSpace(packageCode);
    ImGui.BeginDisabled(!canImport);
    import = ImGui.Button(importingPackage ? "Importing..." : "Load SLP1 Code")
      || import && canImport;
    if (import)
    {
      if (!WorkshopPackageCode.TryDecode(packageCode, out var workshopIds))
        message = "That SLP1 code is invalid";
      else
        ImportPackage(manager, modList, workshopIds).Forget();
    }
    ImGui.EndDisabled();
    ImGuiHelper.ItemTooltip(
      stage == LoadStage.Configuring
        ? "Replace the current enabled mods with this SLP1 code"
        : "SLP1 codes can only be loaded before loading mods",
      hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled);
    ImGuiHelper.TextDisabled("Loading a code changes the list on the left. Save Changes when ready.");
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
    LoadStage stage, ProfileManager manager, ProfileData selected, ModList modList,
    IReadOnlyDictionary<string, ModInfo> modIndex)
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
    ImGuiHelper.TextDisabled("The list on the left is your working copy. Use Save Changes to update this profile.");

    ImGui.PushStyleColor(ImGuiCol.ChildBg, (Vector4)LaunchPadTheme.PanelAlt);
    ImGui.PushStyleColor(ImGuiCol.Border, (Vector4)LaunchPadTheme.Border);
    ImGui.BeginChild("##profilecontents", new Vector2(0, 0), true);
    DrawProfileMods(stage, manager, selected, entries, 0, modList, modIndex);
    if (entries.Count == 0)
      ImGuiHelper.TextDisabled("This profile has no enabled mods.");

    var unavailable = selected.Mods
      .Where(entry => !entry.Enabled && !IsCore(entry)
        && ProfileManager.FindMod(entry, modIndex) == null)
      .ToList();
    if (unavailable.Count > 0)
    {
      ImGui.Separator();
      ImGuiHelper.Text("Unavailable saved mods");
      DrawProfileMods(stage, manager, selected, unavailable, entries.Count, modList, modIndex);
    }
    ImGui.EndChild();
    ImGui.PopStyleColor(2);
  }

  private static void DrawProfileMods(
    LoadStage stage, ProfileManager manager, ProfileData profile,
    IReadOnlyList<ProfileModEntry> entries, int indexOffset, ModList modList,
    IReadOnlyDictionary<string, ModInfo> modIndex)
  {
    if (entries.Count == 0)
      return;

    var rowHeight = ImGui.GetFrameHeightWithSpacing();
    var start = ImGui.GetCursorPos();
    var visibleStart = Math.Max(0,
      (int)((ImGui.GetScrollY() - start.y) / rowHeight) - 1);
    var visibleEnd = Math.Min(entries.Count,
      (int)Math.Ceiling((ImGui.GetScrollY() + ImGui.GetWindowHeight() - start.y) / rowHeight) + 1);

    for (var i = visibleStart; i < visibleEnd; i++)
    {
      ImGui.SetCursorPos(new Vector2(start.x, start.y + i * rowHeight));
      DrawProfileMod(stage, manager, profile, entries[i], indexOffset + i, modList, modIndex);
    }
    ImGui.SetCursorPos(new Vector2(start.x, start.y + entries.Count * rowHeight));
  }

  private static void DrawProfileMod(
    LoadStage stage, ProfileManager manager, ProfileData profile,
    ProfileModEntry entry, int index, ModList modList,
    IReadOnlyDictionary<string, ModInfo> modIndex)
  {
    var mod = ProfileManager.FindMod(entry, modIndex);
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
    message = $"Loading {workshopIds.Count} Workshop mods from SLP1...";
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

      var selectedIds = workshopIds.ToHashSet();
      foreach (var mod in modList.AllMods)
        if (mod.Source != ModSourceType.Core)
          mod.Enabled = mod.Source == ModSourceType.Workshop
            && selectedIds.Contains(mod.WorkshopHandle);

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

      message = $"Loaded {workshopIds.Count} Workshop mods from SLP1";
      LaunchPadConfig.ReloadMods();
    }
    catch (Exception ex)
    {
      Logger.Global.LogException(ex);
      message = "The SLP1 code could not be loaded";
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
