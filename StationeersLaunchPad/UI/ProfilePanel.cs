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
  private static ProfileStatusKind messageKind = ProfileStatusKind.Saved;
  private static string confirmDelete = "";
  private static string confirmEmptySave = "";
  private static string pendingProfileName = "";
  private static string packageCode = "";
  private static string importedProfileName = "";
  private static DateTime confirmationExpires;
  private static bool selectActive;
  private static bool confirmProfileSwitch;
  private static bool importingPackage;
  private static bool importedProfileReady;
  private static readonly HashSet<ulong> subscriptions = [];
  private static ModList indexedModList;
  private static IReadOnlyDictionary<string, ModInfo> modIndex;

  public static bool Busy => subscriptions.Count > 0 || importingPackage;
  public static string BusyText => importingPackage ? "Importing SLP1..." : "Subscribing...";

  public static void SelectActive() => selectActive = true;

  public static void ShowLoadBlocked(string profileName, int missingModCount)
  {
    selectedName = profileName;
    SetMessage(
      $"Loading paused. Restore or remove {missingModCount} missing mod{(missingModCount == 1 ? "" : "s")} from {profileName}.",
      ProfileStatusKind.Error);
  }

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
      changed |= DrawImportedProfileCreate(stage, manager, modList);
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
    ImGuiHelper.Text("Active Mod Profile");

    var selected = manager.FindProfile(selectedName);
    ImGui.SetNextItemWidth(-float.Epsilon);
    if (ImGui.BeginCombo("##profiles", selected?.Name ?? "Disable Profiles"))
    {
      if (ImGui.Selectable("Disable Profiles", selected == null)
        && selected != null)
        changed |= RequestProfileSwitch("", manager, modList);
      foreach (var profile in manager.AllProfiles)
      {
        var missing = manager.GetMissingMods(profile.Name, modList).Count;
        var label = missing > 0
          ? $"{profile.Name}  [{missing} missing]"
          : profile.Name;
        if (missing > 0)
          ImGui.PushStyleColor(
            ImGuiCol.Text, (Vector4)ProfileStatusIndicator.ColorFor(ProfileStatusKind.Error));
        var picked = ImGui.Selectable(label, profile == selected);
        if (missing > 0)
          ImGui.PopStyleColor();
        if (picked && profile != selected)
          changed |= RequestProfileSwitch(profile.Name, manager, modList);
      }
      ImGui.EndCombo();
    }

    changed |= DrawProfileSwitchConfirmation(manager, modList);
    return changed;
  }

  private static bool RequestProfileSwitch(
    string profileName, ProfileManager manager, ModList modList)
  {
    var active = manager.ActiveProfile;
    var hasPendingChanges = active != null
      && manager.HasDiverged(active.Name, modList);
    if (!hasPendingChanges)
      return SwitchProfile(profileName, manager, modList);

    pendingProfileName = profileName;
    confirmProfileSwitch = true;
    ImGui.OpenPopup("Discard unsaved profile changes?##switchprofile");
    return false;
  }

  private static bool DrawProfileSwitchConfirmation(ProfileManager manager, ModList modList)
  {
    const string popupName = "Discard unsaved profile changes?##switchprofile";
    if (confirmProfileSwitch)
      ImGui.OpenPopup(popupName);
    if (!ImGui.BeginPopupModal(popupName,
      ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
      return false;

    var activeName = manager.ActiveProfileName;
    var targetName = string.IsNullOrEmpty(pendingProfileName)
      ? "disabled profiles"
      : pendingProfileName;
    ImGuiHelper.TextWrapped($"{activeName} has unsaved changes. Discard them and switch to {targetName}?");
    ImGui.Spacing();

    var changed = false;
    PushPrimaryButton();
    if (ImGui.Button("Discard and Switch"))
    {
      changed = SwitchProfile(pendingProfileName, manager, modList);
      confirmProfileSwitch = false;
      pendingProfileName = "";
      ImGui.CloseCurrentPopup();
    }
    ImGui.PopStyleColor(3);
    ImGui.SameLine();
    if (ImGui.Button("Cancel"))
    {
      confirmProfileSwitch = false;
      pendingProfileName = "";
      ImGui.CloseCurrentPopup();
    }
    ImGui.EndPopup();
    return changed;
  }

  private static bool SwitchProfile(
    string profileName, ProfileManager manager, ModList modList)
  {
    selectedName = profileName;
    packageCode = "";
    ClearConfirmations();
    if (string.IsNullOrEmpty(profileName))
    {
      manager.DisableProfiles();
      SetMessage("Profiles disabled. The current mod list is unchanged.",
        ProfileStatusKind.Info);
      return false;
    }

    var applied = manager.ApplyProfile(profileName, modList);
    SetMessage(applied ? $"Switched to {profileName}" : "Profile could not be loaded",
      applied ? ProfileStatusKind.Saved : ProfileStatusKind.Error);
    return applied;
  }

  private static void DrawOffState(ProfileManager manager)
  {
    ImGuiHelper.TextWrapped(
      "Profiles are disabled. Select Vanilla to load no mods, create a profile from the list on the left, or choose an existing profile.");
    if (!string.IsNullOrEmpty(manager.ActiveProfileName) && ImGui.Button("Disable Profiles"))
      manager.DisableProfiles();
    DrawMessage();
  }

  private static bool DrawProfileActions(
    LoadStage stage, ProfileManager manager, ModList modList, ProfileData selected,
    IReadOnlyDictionary<string, ModInfo> modIndex)
  {
    var changed = false;
    var status = ProfileStatusIndicator.Evaluate(manager, selected, modList);
    var diverged = manager.HasDiverged(selected.Name, modList, modIndex);
    var missingMods = manager.GetMissingMods(selected.Name, modList);
    var isVanilla = ProfileManager.IsVanillaProfile(selected.Name);
    var hasPendingChanges = diverged;
    ProfileStatusIndicator.Draw(status, prominent: true);
    if (missingMods.Count > 0)
      ImGuiHelper.TextWrapped(
        "Loading is paused. Subscribe to the missing Workshop mods or remove them from the profile below.");

    var emptySave = modList.EnabledMods.All(mod => mod.Source == ModSourceType.Core)
      && selected.Mods.Any(entry => !IsCore(entry));
    if (!emptySave)
      confirmEmptySave = "";
    var saveText = emptySave && confirmEmptySave == selected.Name
      ? "Confirm Save Empty"
      : "Save Changes";
    ImGui.BeginDisabled(!hasPendingChanges || isVanilla);
    if (ImGui.Button(saveText))
    {
      if (emptySave && confirmEmptySave != selected.Name)
      {
        confirmEmptySave = selected.Name;
        confirmationExpires = DateTime.UtcNow.AddSeconds(5);
        SetMessage($"Click again to save {selected.Name} with no enabled mods",
          ProfileStatusKind.Unsaved);
      }
      else
      {
        var saved = manager.UpdateProfile(selected.Name, modList);
        SetMessage(saved ? $"Saved changes to {selected.Name}" : "Profile could not be updated",
          saved ? ProfileStatusKind.Saved : ProfileStatusKind.Error);
        confirmEmptySave = "";
      }
    }
    ImGui.EndDisabled();
    ImGuiHelper.ItemTooltip(
      isVanilla
        ? "Vanilla is built in and cannot be changed. Create a profile to save a custom mod list."
        : hasPendingChanges
        ? $"Update {selected.Name} with the mod states and load order shown on the left."
        : "The working mod list already matches this profile.",
      hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled);

    ImGui.SameLine();
    ImGui.BeginDisabled(!hasPendingChanges);
    if (ImGui.Button("Revert Changes"))
    {
      ClearConfirmations();
      var applied = manager.ApplyProfile(selected.Name, modList);
      changed |= applied;
      SetMessage(applied
          ? $"Reverted to the saved {selected.Name} profile"
          : "Profile could not be loaded",
        applied ? ProfileStatusKind.Saved : ProfileStatusKind.Error);
    }
    ImGui.EndDisabled();
    ImGuiHelper.ItemTooltip(
      $"Discard pending changes and restore the saved {selected.Name} profile.",
      hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled);
    ImGui.SameLine();
    var deleteText = confirmDelete == selected.Name ? "Confirm Delete" : "Delete Profile";
    ImGui.BeginDisabled(isVanilla);
    if (ImGui.Button(deleteText))
    {
      if (confirmDelete != selected.Name)
      {
        confirmDelete = selected.Name;
        confirmationExpires = DateTime.UtcNow.AddSeconds(5);
        SetMessage($"Click again to delete {selected.Name}", ProfileStatusKind.Unsaved);
      }
      else
      {
        var deleted = manager.DeleteProfile(selected.Name);
        SetMessage(deleted ? $"Deleted {selected.Name}" : "Profile could not be deleted",
          deleted ? ProfileStatusKind.Saved : ProfileStatusKind.Error);
        selectedName = "";
        ClearConfirmations();
      }
    }
    ImGui.EndDisabled();
    ImGuiHelper.ItemTooltip(
      isVanilla ? "Vanilla is a built-in profile and cannot be deleted." : $"Delete {selected.Name}.",
      hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled);

    DrawMessage();
    return changed;
  }

  private static void DrawWorkshopPackage(
    LoadStage stage, ProfileManager manager, ModList modList, ProfileData profile,
    IReadOnlyDictionary<string, ModInfo> modIndex)
  {
    var entries = profile?.Mods
      .Where(entry => !IsCore(entry))
      .ToList() ?? [];
    var savedCanCopy = entries.Count > 0 && entries.All(entry =>
      entry.Source == ModSourceType.Workshop && entry.WorkshopHandle > 1);
    var currentMods = modList.EnabledMods
      .Where(mod => mod.Source != ModSourceType.Core)
      .ToList();
    var currentCanCopy = currentMods.Count > 0 && currentMods.All(mod =>
      mod.Source == ModSourceType.Workshop && mod.WorkshopHandle > 1);
    var hasPendingChanges = profile != null
      && manager.HasDiverged(profile.Name, modList, modIndex);
    var canCopy = savedCanCopy && !hasPendingChanges;
    var showCopy = canCopy || currentCanCopy;
    var copyRequirement = profile == null
      ? "Create a profile from the current selection on the Profile tab before copying an SLP1 code."
      : hasPendingChanges
        ? "Use Save Changes on the Profile tab before copying this SLP1 code."
        : "Remove non-Workshop entries from the saved profile before copying an SLP1 code.";

    ImGuiHelper.Text("Shareable SLP1 codes");
    var shareDescription = savedCanCopy
      ? "Copy this Workshop-only profile's mod IDs and load order."
      : profile != null && entries.Count > 0
        ? "Only Workshop-only profiles can be copied. The selected profile does not meet this requirement. You can still load an SLP1 code below."
        : "SLP1 codes share Workshop mod IDs and load order. Paste one below to load it.";
    ImGui.PushStyleColor(ImGuiCol.Text, (Vector4)LaunchPadTheme.TextMuted);
    ImGuiHelper.TextWrapped(shareDescription);
    ImGui.PopStyleColor();
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
        SetMessage("Shareable SLP1 code copied", ProfileStatusKind.Saved);
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
        SetMessage("That SLP1 code is invalid", ProfileStatusKind.Error);
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

    if (!DrawCreateProfileRow("newprofile", ref newProfileName))
      return;

    if (manager.CreateProfile(newProfileName, modList))
    {
      selectedName = newProfileName;
      ClearConfirmations();
      changed |= manager.ApplyProfile(selectedName, modList);
      SetMessage($"Created {selectedName}", ProfileStatusKind.Saved);
      newProfileName = "";
    }
    else
      SetMessage("Use a unique name without special characters", ProfileStatusKind.Error);
  }

  private static bool DrawImportedProfileCreate(
    LoadStage stage, ProfileManager manager, ModList modList)
  {
    if (!importedProfileReady)
      return false;

    ImGui.Separator();
    ImGuiHelper.Text("Save the imported SLP1 mod list");
    ImGui.BeginDisabled(stage != LoadStage.Configuring || Busy);
    var create = DrawCreateProfileRow("importedprofile", ref importedProfileName);
    ImGui.EndDisabled();
    if (!create)
      return false;

    if (!manager.CreateProfile(importedProfileName, modList))
    {
      SetMessage("Use a unique name without special characters", ProfileStatusKind.Error);
      return false;
    }

    selectedName = importedProfileName;
    ClearConfirmations();
    var applied = manager.ApplyProfile(selectedName, modList);
    SetMessage(applied
        ? $"Created and switched to {selectedName}"
        : "Profile was created but could not be loaded",
      applied ? ProfileStatusKind.Saved : ProfileStatusKind.Error);
    if (applied)
    {
      importedProfileName = "";
      importedProfileReady = false;
    }
    return applied;
  }

  private static bool DrawCreateProfileRow(string id, ref string profileName)
  {
    const string buttonText = "Create Profile";
    var style = ImGui.GetStyle();
    var buttonWidth = ImGui.CalcTextSize(buttonText).x + style.FramePadding.x * 2;
    var inputWidth = Math.Max(80f, ImGui.GetContentRegionAvail().x - buttonWidth - style.ItemSpacing.x);

    ImGui.PushID(id);
    ImGui.SetNextItemWidth(inputWidth);
    var create = ImGui.InputTextWithHint(
      "##profilename", "Profile name", ref profileName, 80,
      ImGuiInputTextFlags.EnterReturnsTrue);
    ImGui.SameLine();
    create |= ImGui.Button(buttonText);
    ImGui.PopID();
    return create;
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
      .Where(entry => !IsCore(entry))
      .ToList();
    var currentCount = modList.EnabledMods.Count(mod => mod.Source != ModSourceType.Core);
    ImGuiHelper.Text($"Enabled mods in {selected.Name}");
    ImGui.SameLine();
    ImGuiHelper.TextDisabled($"{entries.Count} saved, {currentCount} currently enabled");
    ImGuiHelper.TextDisabled("The list on the left is your working copy. Use Save Changes to update this profile.");

    ImGui.PushStyleColor(ImGuiCol.ChildBg, (Vector4)LaunchPadTheme.PanelAlt);
    ImGui.PushStyleColor(ImGuiCol.Border, (Vector4)LaunchPadTheme.Border);
    ImGui.BeginChild("##profilecontents", new Vector2(0, 0), true);
    DrawProfileMods(stage, manager, selected, entries, 0, modList, modIndex);
    if (entries.Count == 0)
      ImGuiHelper.TextDisabled("This profile has no enabled mods.");

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
      {
        var removed = manager.RemoveMod(profile.Name, entry);
        SetMessage(removed
            ? $"Removed {name} from {profile.Name}"
            : $"Could not remove {name}",
          removed ? ProfileStatusKind.Saved : ProfileStatusKind.Error);
      }
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

    SetMessage($"Subscribing to Workshop item {entry.WorkshopHandle}...",
      ProfileStatusKind.Info);
    try
    {
      if (!await Steam.SubscribeAndDownload(entry.WorkshopHandle))
      {
        SetMessage($"Could not subscribe to Workshop item {entry.WorkshopHandle}",
          ProfileStatusKind.Error);
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
      workshop.Enabled = true;
      workshop.DirectoryPath = new(entry.DirectoryPath);
      workshop.WorkshopId = new(entry.WorkshopHandle);
      ModConfigUtil.SaveConfig(config);

      SetMessage($"Subscribed to Workshop item {entry.WorkshopHandle}",
        ProfileStatusKind.Saved);
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
    importedProfileReady = false;
    importedProfileName = "";
    SetMessage($"Loading {workshopIds.Count} Workshop mods from SLP1...",
      ProfileStatusKind.Info);
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
          SetMessage($"Could not install Workshop item {workshopId}",
            ProfileStatusKind.Error);
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
      ModConfigUtil.SaveConfig(config);

      SetMessage($"Loaded {workshopIds.Count} Workshop mods from SLP1",
        ProfileStatusKind.Saved);
      importedProfileReady = true;
      LaunchPadConfig.ReloadMods();
    }
    catch (Exception ex)
    {
      Logger.Global.LogException(ex);
      SetMessage("The SLP1 code could not be loaded", ProfileStatusKind.Error);
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
      ProfileStatusIndicator.Draw(messageKind, message);
  }

  private static void SetMessage(string text, ProfileStatusKind kind)
  {
    message = text;
    messageKind = kind;
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
