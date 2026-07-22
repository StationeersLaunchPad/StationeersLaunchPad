using System;
using System.Collections.Generic;
using ImGuiNET;
using StationeersLaunchPad.Loading;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public static class ManualLoadWindow
{
  [Flags]
  public enum ChangeFlags
  {
    None = 0,
    Mods = 1 << 0,
    AutoSort = 1 << 1,
    NextStep = 1 << 2,
  }

  private static LoadStage lastStage;
  private static ModInfo selectedInfo = null;
  private static LoadedMod selectedMod = null;
  private static bool openInfo = false;
  private static ModInfo draggingMod = null;
  private static bool dragged = false;
  private static bool openProfiles = false;

  public static void OpenProfilesTab()
  {
    openProfiles = true;
    ProfilePanel.SelectActive();
  }

  public static void OpenModInfoTab()
  {
    openProfiles = false;
    openInfo = true;
  }

  public static ChangeFlags Draw(LoadStage stage, ModList modList, bool autoSort, ProfileManager profileManager)
  {
    Platform.SetBackgroundEnabled(false);
    var changed = ChangeFlags.None;
    profileManager.Initialize();
    var vanillaActive = ProfileManager.IsVanillaProfile(profileManager.ActiveProfileName);

    // when we move into the loading step, clear the selected mod so all the logs are visible
    if (lastStage < LoadStage.Loaded && stage >= LoadStage.Loading)
      selectedInfo = null;
    lastStage = stage;

    ImGuiHelper.Draw(() =>
    {
      var style = ImGui.GetStyle();
      var windowRect = ImGuiHelper.ScreenRect().Shrink(25f);
      ImGuiHelper.SetNextWindowRect(windowRect);
      ImGui.Begin("##preloadermanual", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);

      var contentRect = ImGuiHelper.AvailableRect();
      contentRect.SplitRY(0.6f, out var topRect, out var bottomRect);
      topRect.SplitRX(0.5f, out var leftRect, out var rightRect);

      leftRect.SplitOY(ImGui.GetTextLineHeight(), out var statusRect, out leftRect);

      if (DrawStatusLine(statusRect, stage, profileManager, modList))
        changed |= ChangeFlags.NextStep;

      ImGui.SetCursorScreenPos(leftRect.Min);
      ImGui.Separator();
      leftRect = leftRect.From(ImGui.GetCursorScreenPos());

      ImGuiHelper.SetNextWindowRect(leftRect);
      ImGui.SetCursorScreenPos(leftRect.Min);
      ImGui.BeginChild("##left", leftRect.Size);

      if (stage is LoadStage.Searching or LoadStage.Configuring)
      {
        if (vanillaActive)
          ImGuiHelper.TextDisabled(
            "Vanilla profile active: choose another profile or Disable Profiles to edit mods.");
        ImGui.BeginDisabled(vanillaActive);
        changed |= DrawModSelectOptions(modList, autoSort);
        ImGui.EndDisabled();
        leftRect = leftRect.From(ImGui.GetCursorScreenPos());
        ImGui.BeginChild("##modselect", leftRect.Size);
        if (DrawModSelectTable(
          modList, stage == LoadStage.Configuring && !vanillaActive, autoSort))
          changed |= ChangeFlags.Mods;
        ImGui.EndChild();
      }
      else if (stage is LoadStage.Loading or LoadStage.Loaded)
      {
        DrawLoadTable(modList);
      }
      ImGui.EndChild();

      ImGuiHelper.SeparatorLine(rightRect.TL, rightRect.BL);
      rightRect = rightRect.Shrink(1, 0, 0, 0);

      ImGui.SetCursorScreenPos(rightRect.Min);
      ImGui.BeginChild("##right", rightRect.Size);
      var tabBorderSize = style.TabBorderSize;
      style.TabBorderSize = 0f;
      if (ImGui.BeginTabBar("##right"))
      {
        DrawModInfoTab(stage);
        DrawModConfigTab(stage);
        if (DrawProfilesTab(stage, profileManager, modList))
          changed |= ChangeFlags.Mods;

        if (BetaProgramsPanel.Draw(stage, modList))
          changed |= ChangeFlags.Mods;

        // If we changed launchpad config and haven't loaded mods yet, mark mods changed to apply disable/sort behaviour
        if (DrawLaunchPadConfigTab(stage) && stage <= LoadStage.Configuring)
          changed |= ChangeFlags.Mods;

        ImGui.EndTabBar();
      }
      style.TabBorderSize = tabBorderSize;
      ImGui.EndChild();

      ImGuiHelper.SeparatorLine(bottomRect.TL, bottomRect.TR);
      bottomRect = bottomRect.Shrink(0, 1, 0, 0);

      bottomRect.SplitOY(-ImGui.GetTextLineHeightWithSpacing(),
        out var logRect, out var consoleRect);

      ImGui.SetCursorScreenPos(logRect.TL);
      ImGui.BeginChild("##logs", logRect.Size);
      LogPanel.DrawConsole(selectedMod?.Logger ?? Logger.Global);
      ImGui.EndChild();

      StartupConsole.DrawInput(consoleRect);

      ImGui.End();
    });
    return changed;
  }

  private static bool DrawStatusLine(
    Rect rect, LoadStage stage, ProfileManager profileManager, ModList modList)
  {
    var next = false;
    var activeProfile = profileManager.ActiveProfile;
    var missingMods = stage == LoadStage.Configuring && activeProfile != null
      ? profileManager.GetMissingMods(activeProfile.Name, modList).Count
      : 0;
    ImGui.SetCursorScreenPos(rect.Min);

    ImGuiHelper.TextDisabled($"SLP {LaunchPadInfo.VERSION}");
    ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextDisabled("|"), true);

    ImGuiHelper.Text(missingMods > 0
      ? $"Loading paused: {activeProfile.Name} is missing {missingMods} required mod{(missingMods == 1 ? "" : "s")}"
      : stage switch
      {
        LoadStage.Updating => "Checking for updates to StationeersLaunchPad",
        LoadStage.Initializing => "Initializing core components",
        LoadStage.Searching => "Locating installed local and workshop mods",
        LoadStage.Configuring => "Ready to load mods",
        LoadStage.Loading => "Loading selected mods",
        LoadStage.Loaded => "Ready to start game",
        LoadStage.Failed => "Mods failed to load. Game may not function properly",
        _ => "",
      });

    rect.SplitOX(-150f, out _, out var buttonRect);
    buttonRect = buttonRect.Shift(-ImGui.GetStyle().ItemSpacing.x, 0f);
    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.zero);
    var (nextEnabled, nextText) = stage switch
    {
      LoadStage.Configuring when ProfilePanel.Busy => (false, ProfilePanel.BusyText),
      LoadStage.Configuring when BetaProgramsPanel.Busy => (false, "Updating Betas..."),
      LoadStage.Configuring when missingMods > 0 =>
        (true, $"Resolve {missingMods} Missing"),
      LoadStage.Configuring => (true, "Load Mods"),
      LoadStage.Loaded or LoadStage.Failed => (true, "Start Game"),
      _ => (false, "..."),
    };

    if (nextEnabled)
      ImGui.PushStyleColor(ImGuiCol.Button,
        ImGuiHelper.FlashColor(ImGuiCol.Button, ImGuiCol.ButtonActive));

    ImGui.SetCursorScreenPos(buttonRect.Min);
    ImGui.BeginDisabled(!nextEnabled);
    if (ImGui.Button(nextText, buttonRect.Size))
      next = true;
    ImGui.EndDisabled();
    if (missingMods > 0)
      ImGuiHelper.ItemTooltip("Restore the missing mods or remove them from the active profile before loading.");
    if (nextEnabled)
      ImGui.PopStyleColor();
    ImGui.PopStyleVar();

    return next;
  }

  private static ChangeFlags DrawModSelectOptions(ModList modList, bool autoSort)
  {
    var changed = ChangeFlags.None;

    ImGui.AlignTextToFramePadding();

    if (ImGui.Checkbox("Auto-sort", ref autoSort))
      changed |= ChangeFlags.AutoSort;

    ImGui.SameLine();
    ImGuiHelper.TextDisabled("|", true);
    ImGui.SameLine();
    ImGuiHelper.Text("Enable mods:");

    const byte hasEnabled = 1;
    const byte hasDisabled = 2;
    const byte hasBoth = hasEnabled | hasDisabled;

    Span<byte> states = stackalloc byte[4];
    foreach (var mod in modList.AllMods)
    {
      if (mod.Source is ModSourceType.Core)
        continue;
      var flag = mod.Enabled ? hasEnabled : hasDisabled;
      states[0] |= flag;
      states[(int)mod.Source] |= flag;
    }
    Span<byte> tgtStates = stackalloc byte[] { hasBoth, hasBoth, hasBoth, hasBoth };

    static bool SelectCheckbox(string label, byte curState, out byte nextState, bool force = false)
    {
      if (curState == 0 && !force)
      {
        nextState = hasBoth;
        return false;
      }
      ImGui.SameLine();
      ImGui.PushItemFlag(ImGuiItemFlags.MixedValue, curState == hasBoth);
      nextState = curState switch
      {
        hasDisabled => hasEnabled,
        _ => hasDisabled,
      };
      var tempState = (curState & hasEnabled) != 0;
      var res = ImGui.Checkbox(label, ref tempState);
      ImGui.PopItemFlag();
      return res;
    }

    ImGui.BeginDisabled(states[0] == 0);
    if (SelectCheckbox("All##enableAll", states[0], out var nextState, force: true))
      tgtStates.Fill(nextState);
    if (SelectCheckbox("Local##enableLocal",
        states[(int)ModSourceType.Local], out nextState))
      tgtStates[(int)ModSourceType.Local] = nextState;
    if (SelectCheckbox("Workshop##enableWorkshop",
        states[(int)ModSourceType.Workshop], out nextState))
      tgtStates[(int)ModSourceType.Workshop] = nextState;
    if (SelectCheckbox("Repo##enableRepo",
        states[(int)ModSourceType.Repo], out nextState))
      tgtStates[(int)ModSourceType.Repo] = nextState;
    ImGui.EndDisabled();

    if ((tgtStates[1] & tgtStates[2] & tgtStates[3]) != hasBoth)
    {
      foreach (var mod in modList.AllMods)
      {
        if (mod.Source is ModSourceType.Core)
          continue;
        var tgtFlags = tgtStates[(int)mod.Source];
        if (tgtFlags == hasBoth)
          continue;
        var tgt = (tgtFlags & hasEnabled) != 0;
        if (mod.Enabled != tgt)
        {
          mod.Enabled = tgt;
          changed |= ChangeFlags.Mods;
        }
      }
    }

    ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.y);
    ImGui.Separator();

    return changed;
  }

  private static bool DrawModSelectTable(
    ModList modList, bool edit = false, bool autoSort = false)
  {
    var changed = false;
    if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
    {
      if (draggingMod != null && !dragged)
      {
        selectedInfo = draggingMod;
        if (selectedInfo != null && selectedInfo.Source == ModSourceType.Core)
          selectedInfo = null;
        openInfo = selectedInfo != null;
      }
      draggingMod = null;
      dragged = false;
    }

    var hoveringIndex = -1;
    var draggingIndex = -1;
    if (draggingMod != null)
      draggingIndex = modList.IndexOf(draggingMod);

    var rowHeight = ImGui.GetTextLineHeightWithSpacing();
    var spacing = ImGui.GetStyle().ItemSpacing.x * 2;
    var available = ImGuiHelper.AvailableRect();

    var row = available.TableRow(
      rowHeight,
      stackalloc[]
      {
        rowHeight + spacing,
        ImGui.CalcTextSize($"{ModSourceType.Workshop}").x + spacing,
      }
    );

    ImGui.BeginDisabled(!edit);

    var idx = 0;
    foreach (var mod in modList.AllMods)
    {
      ImGui.PushID(idx);
      var isBeta = modList.IsBetaMod(mod);

      ImGui.SetCursorScreenPos(row.Column(0).TL);
      ImGui.BeginDisabled(mod.Source is ModSourceType.Core);
      var enabled = mod.Enabled;
      if (ImGui.Checkbox("##enable", ref enabled))
        changed |= BetaProgramsPanel.SetModEnabled(modList, mod, enabled);
      ImGui.EndDisabled();

      var c12 = row.ColumnsFrom(1);
      ImGui.SetCursorScreenPos(c12.Min);
      ImGui.SetNextItemWidth(c12.Size.x);
      ImGui.Selectable($"##rowdrag", mod == draggingMod || (draggingMod == null && mod == selectedInfo));
      if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
      {
        hoveringIndex = idx;
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && draggingMod == null)
        {
          draggingIndex = idx;
          draggingMod = mod;
        }
      }

      ImGuiHelper.TextCentered(row.Column(1), $"{mod.Source}");

      ImGui.SetCursorScreenPos(row.Column(2).TL);
      if (isBeta)
        ImGuiHelper.TextColored($"{mod.Name} [BETA]", ImGuiHelper.Yellow);
      else
        ImGuiHelper.Text(mod.Name);
      if (isBeta)
        ImGuiHelper.ItemTooltip("This item is a beta version of an installed mod.");

      if (draggingMod != null)
        if (mod.SortBefore(draggingMod))
          ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextRightDisabled("Before"));
        else if (draggingMod.SortBefore(mod))
          ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextRightDisabled("After"));

      ImGui.PopID();

      idx++;
      row.NextRow();
    }

    ImGui.EndDisabled();

    if (edit && draggingIndex != -1 && hoveringIndex != -1 && draggingIndex != hoveringIndex)
    {
      dragged = true;
      if (modList.MoveModTo(draggingMod, hoveringIndex, autoSort))
        changed = true;
    }
    return changed;
  }

  private static void DrawLoadTable(ModList modList)
  {
    var style = ImGui.GetStyle();
    var available = ImGuiHelper.AvailableRect();
    var rowHei = ImGui.GetTextLineHeightWithSpacing();

    var spacing = style.ItemSpacing.x * 2;
    var row = available.TableRow(
      ImGui.GetTextLineHeightWithSpacing(),
      stackalloc[] {
      ImGui.CalcTextSize("XXX").x + spacing,
      ImGui.CalcTextSize($"{ModSourceType.Workshop}").x + spacing
    });

    var idx = 0;
    foreach (var mod in ModLoader.LoadedMods)
    {
      ImGui.PushID(idx);
      var info = mod.Info;
      var isSelected = selectedMod == mod;

      ImGui.SetCursorScreenPos(row.Rect.TL);
      if (ImGui.Selectable("##scopeselect", isSelected, row.Rect.Size))
      {
        selectedInfo = isSelected ? null : info;
        selectedMod = isSelected ? null : mod;
      }

      DrawModState(row.Column(0), mod);

      ImGuiHelper.TextCentered(row.Column(1), $"{info.Source}");

      ImGui.SetCursorScreenPos(row.Column(2).TL);
      if (modList.IsBetaMod(info))
        ImGuiHelper.TextColored($"{info.Name} [BETA]", ImGuiHelper.Yellow);
      else
        ImGuiHelper.Text(info.Name);

      ImGui.PopID();
      idx++;

      row.NextRow();
    }
  }

  private static void DrawModState(Rect rect, LoadedMod mod)
  {
    var (text, tooltip) = mod switch
    {
      _ when mod.Info.Source is ModSourceType.Core => ("C", "This mod contains Stationeers' assemblies and data."),
      { LoadFailed: true } => ("X", "This mod is not loaded due to an error that has occurred."),
      { LoadFinished: true } => ("+", "This mod is finished loading."),
      { LoadedEntryPoints: true } => ("...", "This mod is currently loading entrypoints."),
      { LoadedAssets: true } => ("..", "This mod is currently loading assets."),
      { LoadedAssemblies: true } => (".", "This mod is currently loading assemblies."),
      _ => ("...", "This mod is currently loading."),
    };
    ImGuiHelper.TextCentered(rect, text);
    ImGuiHelper.ItemTooltip(tooltip);
  }

  private static void DrawExportButton()
  {
    if (ImGui.Button("Export Server Package"))
      LaunchPadConfig.ExportModPackage();
    ImGuiHelper.ItemTooltip("Package enabled mods into a zip file for dedicated servers.");
  }

  private static void DrawModInfoTab(LoadStage stage)
  {
    var open = ImGui.BeginTabItem("Mod Info", openInfo ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None);
    ImGuiHelper.ItemTooltip("View detailed mod information");
    if (open)
    {
      ImGui.BeginChild("##modinfo", ImGuiWindowFlags.HorizontalScrollbar);
      ModInfoPanel.Draw(selectedInfo);
      ImGui.EndChild();
      ImGui.EndTabItem();
    }
    else if (!openInfo && stage <= LoadStage.Loading)
      selectedInfo = null;
    openInfo = false;
  }

  private static void DrawModConfigTab(LoadStage stage)
  {
    var disabled = stage <= LoadStage.Loading;
    ImGui.BeginDisabled(disabled);
    var open = ImGui.BeginTabItem("Mod Settings");
    ImGui.EndDisabled();
    ImGuiHelper.ItemTooltip(
      disabled ? "Mods must be loaded to edit configuration" : "Edit mod specific configuration",
      hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled
    );
    if (open)
    {
      ConfigPanel.DrawConfigEditor(selectedMod, selectedInfo);
      ImGui.EndTabItem();
    }
  }

  private static bool DrawLaunchPadConfigTab(LoadStage stage)
  {
    var changed = false;
    if (ImGui.BeginTabItem("LaunchPad Settings"))
    {
      DrawAppearanceSettings();
      DrawExportButton();
      DrawAdvancedSettings(stage);
      ImGui.Separator();
      changed = ConfigPanel.DrawConfigFile(Configs.Sorted,
        category => category != "Internal" && category != "Appearance");
      ImGui.EndTabItem();
    }
    return changed;
  }

  private static void DrawAdvancedSettings(LoadStage stage)
  {
    if (!ImGui.CollapsingHeader("Advanced"))
      return;

    var canReload = stage == LoadStage.Configuring && !ProfilePanel.Busy
      && !BetaProgramsPanel.Busy;
    ImGui.BeginDisabled(!canReload);
    if (ImGui.Button("Reload Mod List"))
      LaunchPadConfig.ReloadMods();
    ImGui.EndDisabled();
    ImGuiHelper.ItemTooltip(
      canReload
        ? "Re-scan local, Workshop, and repository mod files from disk."
        : "The mod list can only be reloaded while configuring mods.",
      hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled);
  }

  private static void DrawAppearanceSettings()
  {
    if (!ImGui.CollapsingHeader("Appearance", ImGuiTreeNodeFlags.DefaultOpen))
      return;

    ImGuiHelper.Text("Accent color");
    ImGui.SameLine();
    ImGui.SetNextItemWidth(140f);
    var accent = Configs.UiAccent.Value;
    var open = ImGui.BeginCombo("##accentcolor", accent.ToString());
    ImGuiHelper.ItemTooltip("Classic keeps StationeersLaunchPad's existing colors.");
    if (open)
    {
      foreach (var value in (UiAccentColor[])Enum.GetValues(typeof(UiAccentColor)))
      {
        if (ImGui.Selectable(value.ToString(), value == accent))
          Configs.UiAccent.Value = value;
      }
      ImGui.EndCombo();
    }
  }

  private static bool DrawProfilesTab(LoadStage stage, ProfileManager profileManager, ModList modList)
  {
    var disabled = stage is not LoadStage.Searching and not LoadStage.Configuring;
    ImGui.BeginDisabled(disabled);
    var flags = openProfiles && !disabled ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
    var open = ImGui.BeginTabItem("Mod Profiles", flags);
    ImGui.EndDisabled();
    ImGuiHelper.ItemTooltip(
      disabled ? "Profiles can only be changed before mods load" : "Save and switch local mod configurations",
      hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled
    );
    if (!disabled)
      openProfiles = false;
    if (!open)
      return false;

    ImGui.BeginChild("##profiles");
    var changed = ProfilePanel.Draw(stage, profileManager, modList);
    ImGui.EndChild();
    ImGui.EndTabItem();
    return changed;
  }
}
