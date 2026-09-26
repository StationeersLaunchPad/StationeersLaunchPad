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
  private static string message = "";
  private static ProfileStatusKind messageKind = ProfileStatusKind.Saved;
  private static string messagePack;
  private static string confirmDelete = "";
  private static DateTime confirmationExpires;
  private static string renameFor = "";
  private static string renameText = "";
  private static bool focusRename;
  private static string packageCode = "";
  private static bool importingPackage;
  private static readonly HashSet<ulong> subscriptions = [];
  private static ModList indexedModList;
  private static IReadOnlyDictionary<string, ModInfo> modIndex;

  public static bool Busy => subscriptions.Count > 0 || importingPackage;
  public static string BusyText => importingPackage ? "Importing..." : "Subscribing...";

  public static void SelectActive() { }

  public static void ShowLoadBlocked(string profileName, int missingModCount)
  {
    SetMessage(
      $"Loading paused. Subscribe to or remove {missingModCount} missing mod{(missingModCount == 1 ? "" : "s")} from {profileName}.",
      ProfileStatusKind.Error);
  }

  public static bool Draw(LoadStage stage, ProfileManager manager, ModList modList)
  {
    if (!string.IsNullOrEmpty(confirmDelete) && DateTime.UtcNow > confirmationExpires)
      confirmDelete = "";
    manager.Initialize();
    // a message about another pack is stale once the pack is switched elsewhere
    if (messagePack != null && messagePack != manager.ActiveProfileName)
      message = "";
    messagePack = manager.ActiveProfileName;
    if (!ReferenceEquals(indexedModList, modList))
    {
      indexedModList = modList;
      modIndex = ProfileManager.BuildModIndex(modList.AllMods);
    }

    var changed = false;
    if (!string.IsNullOrEmpty(message))
      ProfileStatusIndicator.Draw(messageKind, message);

    var active = manager.ActiveProfile;
    if (active != null)
      changed |= DrawActivePack(stage, manager, modList, active);

    changed |= DrawAlwaysOn(manager, modList);

    if (Widgets.CollapsibleSection("packs/share", "Share", defaultOpen: false))
      DrawShare(stage, manager, modList, active);

    return changed;
  }

  private static bool DrawActivePack(LoadStage stage, ProfileManager manager, ModList modList, ProfileData active)
  {
    var changed = false;
    var isServer = ProfileManager.IsServerPack(active);
    Widgets.SectionHeader("Active pack");

    ImGui.SetWindowFontScale(1.15f);
    ImGuiHelper.TextColored(active.Name, LaunchPadTheme.Accent);
    ImGui.SetWindowFontScale(1f);
    var count = ProfileManager.ModCount(active);
    ImGui.SameLine();
    ImGui.AlignTextToFramePadding();
    if (!manager.IsBuiltIn(active))
      ImGuiHelper.TextColored($"{count} mod{(count == 1 ? "" : "s")}", LaunchPadTheme.TextMuted);
    ImGui.PushTextWrapPos(0f);
    if (isServer)
      ImGuiHelper.TextColored(
        $"Made when you joined {active.ServerName} and kept in sync with it. Its mods change when the server's do; your always-on mods load on top. Duplicate it to make your own version.",
        LaunchPadTheme.Info);
    else if (manager.IsVanilla(active))
      ImGuiHelper.TextColored("Built in: the game without any mods, not even always-on ones.", LaunchPadTheme.TextSub);
    else if (manager.IsBuiltIn(active))
      ImGuiHelper.TextColored("Built in: the game with only your always-on mods.", LaunchPadTheme.TextSub);
    ImGui.PopTextWrapPos();

    // built-in packs can't be renamed, copied or deleted
    if (manager.IsBuiltIn(active))
      return changed;

    if (renameFor == active.Name)
    {
      ImGui.SetNextItemWidth(Math.Min(320f, ImGui.GetContentRegionAvail().x * 0.5f));
      if (focusRename)
        ImGui.SetKeyboardFocusHere();
      focusRename = false;
      var enter = ImGui.InputText("##rename", ref renameText, 80, ImGuiInputTextFlags.EnterReturnsTrue);
      ImGui.SameLine();
      if (ImGui.Button("Save") || enter)
      {
        if (manager.RenamePack(active.Name, renameText.Trim()))
        {
          SetMessage($"Renamed to {renameText.Trim()}", ProfileStatusKind.Saved);
          renameFor = "";
        }
        else
          SetMessage("That name is taken or has special characters", ProfileStatusKind.Error);
      }
      ImGui.SameLine();
      if (ImGui.Button("Cancel"))
        renameFor = "";
    }
    else
    {
      ImGui.BeginDisabled(stage != LoadStage.Configuring);
      if (ImGui.Button("Rename"))
      {
        renameFor = active.Name;
        renameText = active.Name;
        focusRename = true;
      }
      ImGui.SameLine();
      if (ImGui.Button("Duplicate"))
      {
        var name = manager.UniqueName(isServer ? active.ServerName : active.Name);
        if (manager.CreatePack(name, active) && manager.ApplyProfile(name, modList))
        {
          changed = true;
          SetMessage($"Created {name}, a copy you can edit", ProfileStatusKind.Saved);
        }
        else
          SetMessage("Could not duplicate the pack", ProfileStatusKind.Error);
      }
      ImGuiHelper.ItemTooltip(isServer ? "Make an editable copy of this server pack and switch to it." : "Copy this pack and switch to the copy.");
      ImGui.SameLine();
      var confirming = confirmDelete == active.Name;
      PushDangerButton();
      if (ImGui.Button(confirming ? "Click again to delete" : "Delete"))
      {
        if (!confirming)
        {
          confirmDelete = active.Name;
          confirmationExpires = DateTime.UtcNow.AddSeconds(5);
        }
        else
        {
          var name = active.Name;
          confirmDelete = "";
          if (manager.DeletePack(name, modList))
          {
            changed = true;
            SetMessage($"Deleted {name}, switched to {manager.ActiveProfileName}", ProfileStatusKind.Saved);
          }
          else
            SetMessage($"Could not delete {name}", ProfileStatusKind.Error);
        }
      }
      ImGui.PopStyleColor(3);
      ImGuiHelper.ItemTooltip("Delete this pack. The mods stay installed.");
      ImGui.EndDisabled();
    }

    DrawMissingMods(stage, manager, active, modList);
    return changed;
  }

  private static void DrawMissingMods(LoadStage stage, ProfileManager manager, ProfileData pack, ModList modList)
  {
    var missing = ProfileManager.GetMissingMods(pack, modList);
    if (missing.Count == 0)
      return;

    ImGui.Spacing();
    ImGuiHelper.TextColored($"{missing.Count} mod{(missing.Count == 1 ? " is" : "s are")} not installed", LaunchPadTheme.Err);
    ImGui.PushTextWrapPos(0f);
    ImGuiHelper.TextColored("Loading is paused until they are installed or removed from the pack.", LaunchPadTheme.TextSub);
    ImGui.PopTextWrapPos();

    var workshop = missing.Where(entry => entry.WorkshopHandle > 1).ToList();
    var canSubscribe = stage == LoadStage.Configuring && !Busy;
    if (workshop.Count > 1)
    {
      ImGui.BeginDisabled(!canSubscribe);
      if (ImGui.Button($"Subscribe to all {workshop.Count}"))
        SubscribeAll(workshop).Forget();
      ImGui.EndDisabled();
    }

    var index = 0;
    foreach (var entry in missing)
    {
      ImGui.PushID(index++);
      var name = GetFallbackName(entry);
      ImGui.AlignTextToFramePadding();
      ImGuiHelper.Text(name);
      if (entry.WorkshopHandle > 1)
      {
        ImGui.SameLine();
        var busy = subscriptions.Contains(entry.WorkshopHandle);
        ImGui.BeginDisabled(!canSubscribe);
        if (ImGui.Button(busy ? "Subscribing..." : "Subscribe"))
          SubscribeAll([entry]).Forget();
        ImGui.EndDisabled();
        ImGuiHelper.ItemTooltip($"Subscribe to Workshop item {entry.WorkshopHandle}",
          hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled);
      }
      if (manager.IsEditable(pack))
      {
        ImGui.SameLine();
        if (ImGui.Button("Remove"))
        {
          var removed = manager.RemoveMod(pack.Name, entry);
          SetMessage(removed ? $"Removed {name} from {pack.Name}" : $"Could not remove {name}",
            removed ? ProfileStatusKind.Saved : ProfileStatusKind.Error);
        }
        ImGuiHelper.ItemTooltip($"Remove {name} from {pack.Name}.");
      }
      ImGui.PopID();
    }
  }

  private static bool DrawAlwaysOn(ProfileManager manager, ModList modList)
  {
    var changed = false;
    Widgets.SectionHeader("Always-on mods");
    ImGui.PushTextWrapPos(0f);
    ImGuiHelper.TextColored(
      "These load in every pack except Vanilla, server packs included. Turn a mod on with the power button at the end of its row in the mod list.",
      LaunchPadTheme.TextMuted);
    ImGuiHelper.TextColored(
      "Only for mods that work purely on your side. A mod that the server needs too can break the game or desync when you join a server without it.",
      LaunchPadTheme.Warn);
    ImGui.PopTextWrapPos();

    var entries = manager.AlwaysOn.Mods.Where(entry => entry.Source != ModSourceType.Core).ToList();
    if (entries.Count == 0)
    {
      ImGuiHelper.TextColored("No always-on mods.", LaunchPadTheme.TextSub);
      return false;
    }
    var index = 0;
    foreach (var entry in entries)
    {
      ImGui.PushID(index++);
      var installed = ProfileManager.FindMod(entry, modIndex) != null;
      ImGui.AlignTextToFramePadding();
      ImGuiHelper.TextColored(GetFallbackName(entry), installed ? LaunchPadTheme.Text : LaunchPadTheme.TextMuted);
      if (!installed)
      {
        ImGui.SameLine();
        ImGuiHelper.TextColored("not installed", LaunchPadTheme.TextMuted);
      }
      ImGui.SameLine();
      if (ImGui.SmallButton("Turn off"))
        changed |= manager.RemoveAlwaysOn(entry, modList);
      ImGui.PopID();
    }
    return changed;
  }

  private static void DrawShare(LoadStage stage, ProfileManager manager, ModList modList, ProfileData active)
  {
    var entries = active?.Mods
      .Where(entry => entry.Source != ModSourceType.Core)
      .ToList() ?? [];
    var nonWorkshop = entries.Count(entry => entry.Source != ModSourceType.Workshop || entry.WorkshopHandle <= 1);
    ImGui.PushTextWrapPos(0f);
    ImGuiHelper.TextColored(
      "A share code lists Workshop mods by ID. Anyone with LaunchPad can paste it to get the same pack; missing mods are downloaded.",
      LaunchPadTheme.TextMuted);
    ImGui.PopTextWrapPos();

    var canCopy = entries.Count > nonWorkshop;
    ImGui.BeginDisabled(!canCopy);
    if (ImGui.Button($"Copy code for {active?.Name}"))
    {
      packageCode = WorkshopPackageCode.Encode(entries
        .Where(entry => entry.Source == ModSourceType.Workshop && entry.WorkshopHandle > 1)
        .Select(entry => entry.WorkshopHandle).Distinct().OrderBy(id => id));
      GameManager.Clipboard = packageCode;
      SetMessage("Share code copied to the clipboard", ProfileStatusKind.Saved);
    }
    ImGui.EndDisabled();
    if (nonWorkshop > 0)
    {
      ImGui.SameLine();
      ImGui.AlignTextToFramePadding();
      ImGuiHelper.TextColored(
        $"{nonWorkshop} local mod{(nonWorkshop == 1 ? " is" : "s are")} left out", LaunchPadTheme.Warn);
    }

    ImGui.Spacing();
    ImGui.SetNextItemWidth(Math.Min(480f, ImGui.GetContentRegionAvail().x));
    var enter = ImGui.InputTextWithHint("##packagecode", "Paste a share code", ref packageCode, 16384,
      ImGuiInputTextFlags.EnterReturnsTrue);
    var canImport = stage == LoadStage.Configuring && !Busy && !string.IsNullOrWhiteSpace(packageCode);
    ImGui.SameLine();
    ImGui.BeginDisabled(!canImport);
    if ((ImGui.Button(importingPackage ? "Importing..." : "Import as new pack") || enter) && canImport)
    {
      if (!WorkshopPackageCode.TryDecode(packageCode, out var workshopIds))
        SetMessage("That share code is not valid", ProfileStatusKind.Error);
      else
        ImportPackage(manager, modList, workshopIds).Forget();
    }
    ImGui.EndDisabled();
    ImGuiHelper.ItemTooltip(stage == LoadStage.Configuring
        ? "Download the mods and add them as a new pack."
        : "Codes can only be imported before loading mods.",
      hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled);
  }

  private static async UniTask ImportPackage(ProfileManager manager, ModList modList, List<ulong> workshopIds)
  {
    if (importingPackage)
      return;

    importingPackage = true;
    SetMessage($"Importing {workshopIds.Count} Workshop mods...", ProfileStatusKind.Info);
    try
    {
      var installed = modList.AllMods
        .Where(mod => mod.Source == ModSourceType.Workshop)
        .ToDictionary(mod => mod.WorkshopHandle, mod => mod);
      foreach (var workshopId in workshopIds.Where(id => !installed.ContainsKey(id)))
      {
        if (!await Steam.SubscribeAndDownload(workshopId))
        {
          SetMessage($"Could not download Workshop item {workshopId}", ProfileStatusKind.Error);
          return;
        }
      }

      var workshopBase = SteamTransport.WorkshopType.Mod.GetLocalDirInfo().FullName;
      var entries = workshopIds.Distinct().Select(id => new ProfileModEntry
      {
        Name = installed.TryGetValue(id, out var mod) ? mod.Name ?? "" : "",
        Source = ModSourceType.Workshop,
        WorkshopHandle = id,
        DirectoryPath = installed.TryGetValue(id, out var known) ? known.DirectoryPath : Path.Combine(workshopBase, id.ToString()),
      }).ToList();
      var name = manager.UniqueName("Imported pack");
      if (!manager.CreatePack(name, entries) || !manager.ApplyProfile(name, modList))
      {
        SetMessage("The pack could not be saved", ProfileStatusKind.Error);
        return;
      }
      packageCode = "";
      SetMessage($"Imported as {name}. Rename it above.", ProfileStatusKind.Saved);
      LaunchPadConfig.ReloadMods(preserveSelection: false);
    }
    catch (Exception ex)
    {
      Logger.Global.LogException(ex);
      SetMessage("The share code could not be imported", ProfileStatusKind.Error);
    }
    finally
    {
      importingPackage = false;
    }
  }

  public static void Download(List<ProfileModEntry> entries) => SubscribeAll(entries).Forget();

  private static async UniTask SubscribeAll(List<ProfileModEntry> entries)
  {
    var handles = entries.Select(entry => entry.WorkshopHandle).Where(subscriptions.Add).ToList();
    if (handles.Count == 0)
      return;
    try
    {
      var done = 0;
      foreach (var handle in handles)
      {
        SetMessage($"Subscribing to Workshop item {handle}...", ProfileStatusKind.Info);
        if (!await Steam.SubscribeAndDownload(handle))
        {
          SetMessage($"Could not subscribe to Workshop item {handle}", ProfileStatusKind.Error);
          break;
        }
        done++;
      }
      if (done == handles.Count)
        SetMessage($"Subscribed to {done} Workshop item{(done == 1 ? "" : "s")}", ProfileStatusKind.Saved);
      if (done > 0)
        // the active pack is applied again to the new mod list
        LaunchPadConfig.ReloadMods(preserveSelection: false);
    }
    finally
    {
      foreach (var handle in handles)
        subscriptions.Remove(handle);
    }
  }

  public static string GetFallbackName(ProfileModEntry entry)
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

  private static void SetMessage(string text, ProfileStatusKind kind)
  {
    message = text;
    messageKind = kind;
    messagePack = LaunchPadConfig.ProfileManager.ActiveProfileName;
  }

  private static void PushDangerButton()
  {
    var err = LaunchPadTheme.Err;
    ImGui.PushStyleColor(ImGuiCol.Button, (Vector4)LaunchPadTheme.Over(err, 0.3f));
    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, (Vector4)LaunchPadTheme.Over(err, 0.5f));
    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(err.r, err.g, err.b, 0.5f));
  }
}
