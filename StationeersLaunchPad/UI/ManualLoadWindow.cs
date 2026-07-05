using System;
using System.Collections.Generic;
using System.Linq;
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

  // Mod list sorting/filtering state (see DrawModListTools / DrawSearchAndCount)
  private static ModList.ModSortField sortField = ModList.ModSortField.LoadOrder;
  private static bool sortDescending = false;
  private static bool sortApplyToLoadOrder = false;
  private static string searchText = "";

  private static readonly string[] SortFieldLabels =
    { "Load order", "Name", "Author", "Released", "Updated" };

  // Subtle background tint for alternating list rows (zebra striping).
  private static uint RowAltColor =>
    ImGui.ColorConvertFloat4ToU32(new Color(1f, 1f, 1f, 0.045f));

  public static ChangeFlags Draw(LoadStage stage, ModList modList, bool autoSort)
  {
    Platform.SetBackgroundEnabled(false);
    var changed = ChangeFlags.None;

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

      if (DrawStatusLine(statusRect, stage))
        changed |= ChangeFlags.NextStep;

      ImGui.SetCursorScreenPos(leftRect.Min);
      ImGui.Separator();
      leftRect = leftRect.From(ImGui.GetCursorScreenPos());

      ImGuiHelper.SetNextWindowRect(leftRect);
      ImGui.SetCursorScreenPos(leftRect.Min);
      ImGui.BeginChild("##left", leftRect.Size);

      if (stage is LoadStage.Searching or LoadStage.Configuring)
      {
        changed |= DrawModSelectOptions(modList, autoSort);
        leftRect = leftRect.From(ImGui.GetCursorScreenPos());
        ImGui.BeginChild("##modselect", leftRect.Size);
        if (DrawModSelectTable(modList, stage == LoadStage.Configuring, autoSort))
          changed |= ChangeFlags.Mods;
        ImGui.EndChild();
      }
      else if (stage is LoadStage.Loading or LoadStage.Loaded)
      {
        DrawModListToolbar(modList, loaded: true);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.y);
        ImGui.Separator();
        leftRect = leftRect.From(ImGui.GetCursorScreenPos());
        ImGui.BeginChild("##loadtable", leftRect.Size);
        DrawLoadTable(modList);
        ImGui.EndChild();
      }
      ImGui.EndChild();

      ImGuiHelper.SeparatorLine(rightRect.TL, rightRect.BL);
      rightRect = rightRect.Shrink(1, 0, 0, 0);

      ImGui.SetCursorScreenPos(rightRect.Min);
      ImGui.BeginChild("##right", rightRect.Size);
      if (ImGui.BeginTabBar("##right"))
      {
        DrawModInfoTab(stage);
        DrawModConfigTab(stage);

        // If we changed launchpad config and haven't loaded mods yet, mark mods changed to apply disable/sort behaviour
        if (DrawLaunchPadConfigTab() && stage <= LoadStage.Configuring)
          changed |= ChangeFlags.Mods;

        ImGui.EndTabBar();
      }
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

  private static bool DrawStatusLine(Rect rect, LoadStage stage)
  {
    var next = false;
    ImGui.SetCursorScreenPos(rect.Min);

    ImGuiHelper.TextDisabled(LaunchPadInfo.VERSION);
    ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextDisabled("|"), true);

    ImGuiHelper.Text(stage switch
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
    if (nextEnabled)
      ImGui.PopStyleColor();
    ImGui.PopStyleVar();

    return next;
  }

  private static ChangeFlags DrawModSelectOptions(ModList modList, bool autoSort)
  {
    var changed = ChangeFlags.None;

    ImGui.AlignTextToFramePadding();

    if (ImGui.Checkbox("AutoSort", ref autoSort))
      changed |= ChangeFlags.AutoSort;

    ImGui.SameLine();
    ImGuiHelper.TextDisabled("|", true);
    ImGui.SameLine();
    ImGuiHelper.Text("Enable:");

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

    changed |= DrawModListToolbar(modList, loaded: false);

    ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.y);
    ImGui.Separator();

    return changed;
  }

  // Full toolbar above the mod list: sort controls, search box, counters and column headers.
  private static ChangeFlags DrawModListToolbar(ModList modList, bool loaded)
  {
    var changed = DrawModListTools(modList, loaded);
    DrawSearchAndCount(modList, loaded);
    DrawColumnHeader(loaded);
    return changed;
  }

  // Sort selector: field combo, direction toggle and an optional "apply to load order" switch.
  // When loaded is true the mods are already loaded, so the load-order switch is hidden and
  // sorting only changes how the list is displayed.
  private static ChangeFlags DrawModListTools(ModList modList, bool loaded)
  {
    var changed = ChangeFlags.None;

    ImGui.AlignTextToFramePadding();
    ImGuiHelper.Text("Sort:");

    ImGui.SameLine();
    ImGui.SetNextItemWidth(140f);
    var cur = (int)sortField;
    if (ImGui.Combo("##sortfield", ref cur, SortFieldLabels, SortFieldLabels.Length))
    {
      sortField = (ModList.ModSortField)cur;
      if (!loaded && sortApplyToLoadOrder && modList.ApplySort(sortField, sortDescending))
        changed |= ChangeFlags.Mods;
    }
    ImGuiHelper.ItemTooltip(
      "Order the mod list by load order, name, author, release date or last update.\n" +
      "Release/update dates come from Steam for Workshop mods; for local mods the mod " +
      "folder timestamps are used as a fallback.");

    ImGui.SameLine();
    if (ImGui.Button(sortDescending ? "Desc" : "Asc"))
    {
      sortDescending = !sortDescending;
      if (!loaded && sortApplyToLoadOrder && modList.ApplySort(sortField, sortDescending))
        changed |= ChangeFlags.Mods;
    }
    ImGuiHelper.ItemTooltip("Toggle ascending/descending order.");

    if (!loaded)
    {
      ImGui.SameLine();
      if (ImGui.Checkbox("Apply to load order", ref sortApplyToLoadOrder)
          && sortApplyToLoadOrder
          && modList.ApplySort(sortField, sortDescending))
        changed |= ChangeFlags.Mods;
      ImGuiHelper.ItemTooltip(
        "On: sorting also rewrites the real mod load order (drag & drop is disabled while a sort is active).\n" +
        "Off: sorting only changes how the list is displayed; the load order is left untouched.");
    }

    ImGui.SameLine();
    ImGuiHelper.TextDisabled("|", true);

    ImGui.SameLine();
    if (ImGui.Button("Export List"))
      LaunchPadConfig.ExportModListJson();
    ImGuiHelper.ItemTooltip($"Save the current mod list (state + order) as JSON to:\n{LaunchPadPaths.ModListJsonPath}");

    ImGui.SameLine();
    ImGui.BeginDisabled(!LaunchPadConfig.CanImportModList);
    if (ImGui.Button("Import List"))
    {
      LaunchPadConfig.ImportModListJson();
      changed |= ChangeFlags.Mods;
    }
    ImGui.EndDisabled();
    ImGuiHelper.ItemTooltip(
      $"Load a mod list from JSON and apply its enabled state and order:\n{LaunchPadPaths.ModListJsonPath}",
      hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled);

    return changed;
  }

  private static string SortValueText(ModInfo mod) => sortField switch
  {
    ModList.ModSortField.Author => string.IsNullOrEmpty(mod.Author) ? "-" : mod.Author,
    ModList.ModSortField.Released => mod.Released?.ToString("yyyy-MM-dd") ?? "-",
    ModList.ModSortField.Updated => mod.Updated?.ToString("yyyy-MM-dd") ?? "-",
    _ => null,
  };

  // True when the mod matches the current search text (by name or author).
  private static bool MatchesSearch(ModInfo mod)
  {
    if (string.IsNullOrWhiteSpace(searchText))
      return true;
    var q = searchText.Trim();
    return (mod.Name?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
        || (mod.Author?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
  }

  // Search box + counters row.
  private static void DrawSearchAndCount(ModList modList, bool loaded)
  {
    ImGui.AlignTextToFramePadding();
    ImGuiHelper.Text("Search:");

    ImGui.SameLine();
    ImGui.SetNextItemWidth(200f);
    ImGui.InputTextWithHint("##modsearch", "name or author...", ref searchText, 256);

    if (!string.IsNullOrEmpty(searchText))
    {
      ImGui.SameLine();
      if (ImGui.Button("Clear##search"))
        searchText = "";
    }

    int total = 0, enabled = 0, shown = 0;
    if (loaded)
    {
      total = enabled = ModLoader.LoadedMods.Count;
      shown = ModLoader.LoadedMods.Count(m => MatchesSearch(m.Info));
    }
    else
    {
      foreach (var mod in modList.AllMods)
      {
        if (mod.Source == ModSourceType.Core)
          continue;
        total++;
        if (mod.Enabled)
          enabled++;
        if (MatchesSearch(mod))
          shown++;
      }
    }

    ImGui.SameLine();
    ImGuiHelper.TextDisabled("|", true);
    ImGui.SameLine();
    var countText = loaded ? $"Loaded: {total}" : $"Enabled: {enabled} / {total}";
    if (!string.IsNullOrWhiteSpace(searchText))
      countText += $"   (showing {shown})";
    ImGuiHelper.TextDisabled(countText);
  }

  // Fixed (non-scrolling) column headers aligned with the table columns below.
  private static void DrawColumnHeader(bool loaded)
  {
    var spacing = ImGui.GetStyle().ItemSpacing.x * 2;
    var c0 = loaded
      ? ImGui.CalcTextSize("XXX").x + spacing
      : ImGui.GetTextLineHeightWithSpacing() + spacing;
    var c1 = ImGui.CalcTextSize($"{ModSourceType.Workshop}").x + spacing;

    var available = ImGuiHelper.AvailableRect();
    var row = available.TableRow(ImGui.GetTextLineHeightWithSpacing(), stackalloc[] { c0, c1 });

    ImGuiHelper.TextCentered(row.Column(1), "Source");

    var nameHeader = "Name";
    if (sortField != ModList.ModSortField.LoadOrder)
      nameHeader = $"Name   (by {SortFieldLabels[(int)sortField]} {(sortDescending ? "v" : "^")})";
    ImGuiHelper.Text(row.Column(2), nameHeader);
  }

  private static bool DrawModSelectTable(ModList modList, bool edit = false, bool autoSort = false)
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

    // Display order may differ from the real load order when a sort/search is active.
    // Drag & drop reordering is only allowed when showing the full, unsorted load order.
    var displayMods = modList.Sorted(sortField, sortDescending);
    if (!string.IsNullOrWhiteSpace(searchText))
      displayMods = displayMods.Where(MatchesSearch).ToList();
    var canReorder = edit
      && sortField == ModList.ModSortField.LoadOrder
      && string.IsNullOrWhiteSpace(searchText);

    var hoveringIndex = -1;
    var draggingIndex = -1;
    if (draggingMod != null)
      draggingIndex = displayMods.IndexOf(draggingMod);

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
    foreach (var mod in displayMods)
    {
      ImGui.PushID(idx);

      if ((idx & 1) == 1)
        ImGui.GetWindowDrawList().AddRectFilled(row.Rect.TL, row.Rect.BR, RowAltColor);

      ImGui.SetCursorScreenPos(row.Column(0).TL);
      ImGui.BeginDisabled(mod.Source is ModSourceType.Core);
      if (ImGui.Checkbox("##enable", ref mod.Enabled))
        changed = true;
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

      ImGuiHelper.Text(row.Column(2), $"{mod.Name}");

      if (draggingMod != null && canReorder)
      {
        if (mod.SortBefore(draggingMod))
          ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextRightDisabled("Before"));
        else if (draggingMod.SortBefore(mod))
          ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextRightDisabled("After"));
      }
      else
      {
        var sortValue = SortValueText(mod);
        if (sortValue != null)
          ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextRightDisabled(sortValue));
      }

      ImGui.PopID();

      idx++;
      row.NextRow();
    }

    ImGui.EndDisabled();

    if (canReorder && draggingIndex != -1 && hoveringIndex != -1 && draggingIndex != hoveringIndex)
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

    // Apply the same view sorting/search as the pre-load list. The actual load order is
    // fixed at this point, so this only changes how the loaded mods are displayed.
    IEnumerable<LoadedMod> loadedMods = ModLoader.LoadedMods;
    if (sortField != ModList.ModSortField.LoadOrder)
    {
      var cmp = ModList.GetComparer(sortField, sortDescending);
      var sorted = new List<LoadedMod>(ModLoader.LoadedMods);
      sorted.Sort((a, b) => cmp.Compare(a.Info, b.Info));
      loadedMods = sorted;
    }
    if (!string.IsNullOrWhiteSpace(searchText))
      loadedMods = loadedMods.Where(m => MatchesSearch(m.Info));

    var idx = 0;
    foreach (var mod in loadedMods)
    {
      ImGui.PushID(idx);
      var info = mod.Info;
      var isSelected = selectedMod == mod;

      if ((idx & 1) == 1)
        ImGui.GetWindowDrawList().AddRectFilled(row.Rect.TL, row.Rect.BR, RowAltColor);

      ImGui.SetCursorScreenPos(row.Rect.TL);
      if (ImGui.Selectable("##scopeselect", isSelected, row.Rect.Size))
      {
        selectedInfo = isSelected ? null : info;
        selectedMod = isSelected ? null : mod;
      }

      DrawModState(row.Column(0), mod);

      ImGuiHelper.TextCentered(row.Column(1), $"{info.Source}");

      ImGuiHelper.Text(row.Column(2), info.Name);

      var sortValue = SortValueText(info);
      if (sortValue != null)
        ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextRightDisabled(sortValue));

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
    if (ImGui.Button("Export Mod Package"))
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
    var open = ImGui.BeginTabItem("Mod Configuration");
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

  private static bool DrawLaunchPadConfigTab()
  {
    var changed = false;
    if (ImGui.BeginTabItem("LaunchPad Configuration"))
    {
      DrawExportButton();
      changed = ConfigPanel.DrawConfigFile(Configs.Sorted, category => category != "Internal");
      ImGui.EndTabItem();
    }
    return changed;
  }
}
