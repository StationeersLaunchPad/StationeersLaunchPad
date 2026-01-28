using ImGuiNET;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;
using System;
using UnityEngine;

namespace StationeersLaunchPad.UI
{
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
    private static bool openInfo = false;
    private static ModInfo draggingMod = null;
    private static bool dragged = false;

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
          DrawLoadTable(modList);
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
        bottomRect = bottomRect.Shrink(0,1,0,0);

        ImGui.SetCursorScreenPos(bottomRect.TL);
        ImGui.BeginChild("##logs", bottomRect.Size);
        LogPanel.DrawConsole(selectedInfo?.Loaded?.Logger ?? Logger.Global);
        ImGui.EndChild();

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

      const int hasEnabled = 1;
      const int hasDisabled = 2;
      const int hasBoth = hasEnabled | hasDisabled;
      var allState = 0;
      var localState = 0;
      var workshopState = 0;

      foreach (var mod in modList.AllMods)
      {
        if (mod.Source is ModSourceType.Core)
          continue;
        var flag = mod.Enabled ? hasEnabled : hasDisabled;
        allState |= flag;
        if (mod.Source is ModSourceType.Local)
          localState |= flag;
        else
          workshopState |= flag;
      }

      var tgtLocal = hasBoth;
      var tgtWorkshop = hasBoth;

      bool SelectCheckbox(string label, int curState, out int nextState, bool force = false)
      {
        if (curState == 0 && !force)
        {
          nextState = 0;
          return false;
        }
        ImGui.SameLine();
        ImGui.PushItemFlag(ImGuiItemFlags.MixedValue, curState == hasBoth);
        nextState = curState switch
        {
          hasEnabled => hasDisabled,
          _ => hasEnabled,
        };
        var tempState = (curState & hasEnabled) != 0;
        var res = ImGui.Checkbox(label, ref tempState);
        ImGui.PopItemFlag();
        return res;
      }

      ImGui.BeginDisabled(allState == 0);
      if (SelectCheckbox("All##enableAll", allState, out var nextState, force: true))
        tgtLocal = tgtWorkshop = nextState;
      if (SelectCheckbox("Local##enableLocal", localState, out nextState))
        tgtLocal = nextState;
      if (SelectCheckbox("Workshop##enableWorkshop", workshopState, out nextState))
        tgtWorkshop = nextState;
      ImGui.EndDisabled();

      if (tgtLocal != hasBoth || tgtWorkshop != hasBoth)
      {
        foreach (var mod in modList.AllMods)
        {
          if (mod.Source is ModSourceType.Core)
            continue;
          var tgtFlags = mod.Source is ModSourceType.Workshop ? tgtWorkshop : tgtLocal;
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
      foreach (var info in modList.EnabledMods)
      {
        ImGui.PushID(idx);
        var mod = info.Loaded;
        var isSelected = selectedInfo == info;

        ImGui.SetCursorScreenPos(row.Rect.TL);
        if (ImGui.Selectable("##scopeselect", isSelected, row.Rect.Size))
          selectedInfo = isSelected ? null : info;

        DrawModState(row.Column(0), info);

        ImGuiHelper.TextCentered(row.Column(1), $"{info.Source}");

        ImGuiHelper.Text(row.Column(2), info.Name);

        ImGui.PopID();
        idx++;

        row.NextRow();
      }
    }

    private static void DrawModState(Rect rect, ModInfo info)
    {
      var mod = info?.Loaded;

      var (text, tooltip) = mod switch
      {
        _ when info.Source is ModSourceType.Core => ("C", "This mod contains Stationeers' assemblies and data."),
        null => ("-", "This mod is contains no assemblies to load or an error has occurred."),
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
        ConfigPanel.DrawConfigEditor(selectedInfo);
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
}
