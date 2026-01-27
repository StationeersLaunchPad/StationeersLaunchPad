using BepInEx;
using ImGuiNET;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;
using System;
using UnityEngine;

namespace StationeersLaunchPad.UI
{
  public static class LoaderPanel
  {
    [Flags]
    public enum ChangeFlags
    {
      None = 0,
      Mods = 1 << 0,
      AutoSort = 1 << 1,
      NextStep = 1 << 2,
    }

    private static LoadStage lastState;
    private static ModInfo selectedInfo = null;
    private static bool openLogs = false;
    private static bool openInfo = false;
    private static ModInfo draggingMod = null;
    private static bool dragged = false;

    // returns true if the user clicked to stop autoloading
    public static bool DrawAutoLoad(LoadStage loadState, StageWait wait)
    {
      var stopAuto = false;
      ImGuiHelper.Draw(() =>
      {
        ImGuiHelper.DrawWithPadding(() => ImGui.Begin("##preloaderauto", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings));

        ImGuiHelper.Text($"StationeersLaunchPad {LaunchPadInfo.VERSION}");
        ImGuiHelper.Text(loadState switch
        {
          LoadStage.Updating => "Checking for Update",
          LoadStage.Initializing => "Initializing",
          LoadStage.Searching => "Finding Mods",
          LoadStage.Configuring => $"Loading Mods in {wait.SecondsRemaining:0.0}s",
          LoadStage.Loading => "Loading Mods",
          LoadStage.Loaded => $"Starting game in {wait.SecondsRemaining:0.0}s",
          LoadStage.Running => "Game Running",
          LoadStage.Failed => "Loading Failed",
          _ => throw new ArgumentOutOfRangeException(),
        });

        ImGui.Spacing();
        var line = Logger.Global.Last();
        if (line != null)
          LogPanel.DrawConsoleLine(line, true);
        else
          ImGuiHelper.Text("");

        ImGuiHelper.DrawIfHovering(() =>
        {
          ImGuiHelper.ItemTooltip("Click to pause loading.");
          if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            stopAuto = true;
        });

        ImGui.End();
      });

      return stopAuto;
    }

    public static ChangeFlags DrawManualLoad(LoadStage loadState, ModList modList, bool autoSort)
    {
      var changed = ChangeFlags.None;

      // when we move into the loading step, clear the selected mod so all the logs are visible
      if (lastState < LoadStage.Loaded && loadState >= LoadStage.Loading)
        selectedInfo = null;
      lastState = loadState;

      ImGuiHelper.Draw(() =>
      {
        ImGuiHelper.DrawWithPadding2(() => ImGui.Begin("##preloadermanual", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings));

        var startCursor = ImGui.GetCursorScreenPos();
        var startAvail = ImGui.GetContentRegionAvail();
        string nextStepText = null;

        void DrawBreadcrumbSep() => ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextDisabled(">"), true);
        void DrawBreadcrumb(string text, LoadStage forState, string tooltip, string setNext = null)
        {
          var match = forState == loadState;
          ImGuiHelper.TextDisabled(text, !match);
          ImGuiHelper.ItemTooltip(tooltip);
          if (match)
            nextStepText = setNext;
        }

        ImGuiHelper.TextDisabled(LaunchPadInfo.VERSION);
        ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextDisabled("|"), true);

        if (Configs.CheckForUpdate.Value)
        {
          DrawBreadcrumb("Update", LoadStage.Updating, "Checking for updates to StationeersLaunchPad");
          DrawBreadcrumbSep();
        }

        DrawBreadcrumb("Initalize", LoadStage.Initializing, "Initializing core components");
        DrawBreadcrumbSep();

        DrawBreadcrumb("Locate Mods", LoadStage.Searching, "Locating installed local and workshop mods");
        DrawBreadcrumbSep();

        DrawBreadcrumb("Select Mods", LoadStage.Configuring, "Ready to load mods", "Load Mods");
        DrawBreadcrumbSep();

        DrawBreadcrumb("Loading Mods", LoadStage.Loading, "Loading selected mods");
        DrawBreadcrumbSep();

        if (loadState == LoadStage.Failed)
          DrawBreadcrumb("Loading Failed", LoadStage.Failed,
            "Mods failed to load. Game may not function properly", "Start Game");
        else
          DrawBreadcrumb("Mods Loaded", LoadStage.Loaded, "Ready to start game", "Start Game");

        {
          var style = ImGui.GetStyle();
          var spacing = style.ItemSpacing.x;
          var padding = style.FramePadding.x;
          var right = startCursor.x + startAvail.x / 2f - spacing;
          var height = ImGui.GetTextLineHeight();

          ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.zero);
          var canNextStep = nextStepText != null;
          nextStepText ??= "...";

          if (canNextStep)
          {
            var flashPos = Mathf.Sin(Time.realtimeSinceStartup * 5f) * 0.5f + 0.5f;
            var crButton = style.Colors[(int) ImGuiCol.Button];
            var crButtonActive = style.Colors[(int) ImGuiCol.ButtonActive];
            ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Lerp(crButton, crButtonActive, flashPos));
          }

          ImGui.SetCursorScreenPos(new(right - 150f, startCursor.y));
          ImGui.BeginDisabled(!canNextStep);
          if (ImGui.Button(nextStepText, new(150f, height)))
          {
            changed |= ChangeFlags.NextStep;
            openLogs = true;
          }
          ImGui.EndDisabled();
          if (canNextStep)
            ImGui.PopStyleColor();
          ImGui.PopStyleVar();
        }
        ImGui.Separator();

        ImGui.BeginColumns("##columns", 2, ImGuiOldColumnFlags.NoResize | ImGuiOldColumnFlags.GrowParentContentsSize);
        ImGui.BeginChild("##left");

        switch (loadState)
        {
          case LoadStage.Initializing:
          case LoadStage.Searching:
          case LoadStage.Updating:
          case LoadStage.Configuring:
          {
            if (loadState == LoadStage.Initializing)
            {
              ImGuiHelper.Text("");
              ImGui.Spacing();
            }
            else
            {
              ImGui.AlignTextToFramePadding();

              if (ConfigPanel.DrawConfigEntry(Configs.AutoSortOnStart))
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
            }

            if (DrawConfigTable(modList, loadState == LoadStage.Configuring, autoSort))
              changed |= ChangeFlags.Mods;
            break;
          }

          case LoadStage.Loading:
          case LoadStage.Loaded:
          {
            DrawLoadTable(modList);
            break;
          }

          default:
          case LoadStage.Running:
          case LoadStage.Failed:
            break;
        }
        ImGui.EndChild();

        ImGui.NextColumn();
        ImGui.SetCursorPosY(ImGui.GetStyle().ItemSpacing.y * 2);
        if (ImGui.BeginTabBar("##right"))
        {
          if (ImGui.BeginTabItem("Logs", openLogs ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
          {
            LogPanel.DrawConsole(selectedInfo?.Loaded?.Logger ?? Logger.Global);
            ImGui.EndTabItem();
          }
          openLogs = false;

          var open = ImGui.BeginTabItem("Mod Info", openInfo ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None);
          ImGuiHelper.ItemTooltip("View detailed mod information");
          if (open)
          {
            ImGui.BeginChild("##modinfo", ImGuiWindowFlags.HorizontalScrollbar);
            DrawModInfo();
            ImGui.EndChild();
            ImGui.EndTabItem();
          }
          else if (!openInfo && loadState <= LoadStage.Loading)
            selectedInfo = null;
          openInfo = false;

          var disabled = loadState <= LoadStage.Loading;
          ImGui.BeginDisabled(disabled);
          open = ImGui.BeginTabItem("Mod Configuration");
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

          if (ImGui.BeginTabItem("LaunchPad Configuration"))
          {
            DrawExportButton();
            var configChanged = ConfigPanel.DrawConfigFile(Configs.Sorted, category => category != "Internal");
            // If we changed launchpad config and haven't loaded mods yet, mark mods changed to apply disable/sort behaviour
            if (loadState <= LoadStage.Configuring && configChanged)
              changed |= ChangeFlags.Mods;

            ImGui.EndTabItem();
          }

          ImGui.EndTabBar();
        }

        ImGui.EndColumns();
        ImGui.End();
      });
      return changed;
    }

    private static bool DrawConfigTable(ModList modList, bool edit = false, bool autoSort = false)
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

      if (!edit)
        ImGui.BeginDisabled();

      if (ImGui.BeginTable("##configtable", 3, ImGuiTableFlags.SizingFixedFit))
      {
        ImGui.TableSetupColumn("##enabled");
        ImGui.TableSetupColumn("##type");
        ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthStretch);

        var idx = 0;
        foreach (var mod in modList.AllMods)
        {
          ImGui.TableNextRow();
          ImGui.PushID(idx);
          ImGui.TableNextColumn();

          if (ImGui.Checkbox("##enable", ref mod.Enabled))
            changed = true;

          ImGui.TableNextColumn();
          ImGui.Selectable($"##rowdrag", mod == draggingMod || (draggingMod == null && mod == selectedInfo), ImGuiSelectableFlags.SpanAllColumns);
          if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
          {
            hoveringIndex = idx;
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && draggingMod == null)
            {
              draggingIndex = idx;
              draggingMod = mod;
            }
          }

          ImGuiHelper.DrawSameLine(() => ImGuiHelper.Text($"{mod.Source}"));

          ImGui.TableNextColumn();
          ImGuiHelper.Text($"{mod.Name}");

          if (draggingMod != null)
            if (mod.SortBefore(draggingMod))
              ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextRightDisabled("Before"));
            else if (draggingMod.SortBefore(mod))
              ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextRightDisabled("After"));

          ImGui.PopID();

          idx++;
        }
        ImGui.EndTable();
      }

      if (!edit)
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
      if (ImGui.BeginTable("##loadtable", 3, ImGuiTableFlags.SizingFixedFit))
      {
        ImGui.TableSetupColumn("##state", 30f);
        ImGui.TableSetupColumn("##type");
        ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthStretch);
        var idx = 0;
        foreach (var info in modList.EnabledMods)
        {
          var mod = info.Loaded;

          ImGui.PushID(idx);

          ImGui.TableNextRow();
          ImGui.TableNextColumn();

          DrawModState(info);

          ImGui.TableNextColumn();
          var isSelected = selectedInfo == info;
          if (ImGui.Selectable("##scopeselect", isSelected, ImGuiSelectableFlags.SpanAllColumns))
          {
            selectedInfo = isSelected ? null : info;
          }
          ImGuiHelper.DrawSameLine(() => ImGuiHelper.Text($"{info.Source}"));

          ImGui.TableNextColumn();
          ImGuiHelper.Text(info.Name);

          ImGui.PopID();
          idx++;
        }
      }
      ImGui.EndTable();
    }

    private static void DrawModState(ModInfo info)
    {
      var mod = info?.Loaded;

      if (info.Source == ModSourceType.Core)
      {
        ImGuiHelper.Text("C");
        ImGuiHelper.ItemTooltip("This mod contains Stationeers' assemblies and data.");
      }
      else if (mod == null)
      {
        ImGuiHelper.Text("-");
        ImGuiHelper.ItemTooltip("This mod is contains no assemblies to load or an error has occurred.");
      }
      else if (mod.LoadFailed)
      {
        ImGuiHelper.TextError("X");
        ImGuiHelper.ItemTooltip("This mod is not loaded due to an error that has occurred.");
      }
      else if (mod.LoadFinished)
      {
        ImGuiHelper.TextSuccess("+");
        ImGuiHelper.ItemTooltip("This mod is finished loading.");
      }
      else if (mod.LoadedEntryPoints)
      {
        ImGuiHelper.Text("...");
        ImGuiHelper.ItemTooltip("This mod is currently loading entrypoints.");
      }
      else if (mod.LoadedAssets)
      {
        ImGuiHelper.Text("..");
        ImGuiHelper.ItemTooltip("This mod is currently loading assets.");
      }
      else if (mod.LoadedAssemblies)
      {
        ImGuiHelper.Text(".");
        ImGuiHelper.ItemTooltip("This mod is currently loading assemblies.");
      }
      else
      {
        ImGuiHelper.Text("...");
        ImGuiHelper.ItemTooltip("This mod is currently loading.");
      }
    }

    private static void DrawExportButton()
    {
      if (ImGui.Button("Export Mod Package"))
        LaunchPadConfig.ExportModPackage();
      ImGuiHelper.ItemTooltip("Package enabled mods into a zip file for dedicated servers.");
    }

    private static void DrawModInfo()
    {
      if (selectedInfo == null)
      {
        ImGuiHelper.TextDisabled("Selected a mod to view detailed info");
        return;
      }

      var about = selectedInfo.About;

      ImGuiHelper.Text(selectedInfo.Name);

      if (ImGui.Button("Open Local Folder"))
        ProcessUtil.OpenExplorerDir(selectedInfo.DirectoryPath);

      if (selectedInfo.WorkshopHandle > 1)
      {
        ImGui.SameLine();
        if (ImGui.Button("Open Workshop Page"))
          Steam.OpenWorkshopPage(selectedInfo.WorkshopHandle);
      }

      ImGui.Spacing();
      ImGuiHelper.Text("Source:");
      ImGui.SameLine();
      ImGuiHelper.Text(selectedInfo.Source.ToString());

      if (!string.IsNullOrEmpty(selectedInfo.DirectoryPath))
      {
        ImGui.Spacing();
        ImGuiHelper.Text("Path:");
        ImGui.SameLine();
        ImGuiHelper.Text(selectedInfo.DirectoryPath);
      }

      if (about == null)
      {
        ImGui.Spacing();
        ImGuiHelper.TextDisabled("Missing About.xml");
        return;
      }

      if (!string.IsNullOrEmpty(selectedInfo.ModID))
      {
        ImGui.Spacing();
        ImGuiHelper.Text("ModID:");
        ImGui.SameLine();
        ImGuiHelper.Text($"{selectedInfo.ModID}");
      }

      if (selectedInfo.WorkshopHandle > 1)
      {
        ImGui.Spacing();
        ImGuiHelper.Text("Workshop ID:");
        ImGui.SameLine();
        ImGuiHelper.Text($"{selectedInfo.WorkshopHandle}");
      }

      if (!string.IsNullOrEmpty(about.Author))
      {
        ImGui.Spacing();
        ImGuiHelper.Text("Author:");
        ImGui.SameLine();
        ImGuiHelper.Text(about.Author.Trim());
      }

      if (!string.IsNullOrEmpty(about.Version))
      {
        ImGui.Spacing();
        ImGuiHelper.Text("Version:");
        ImGui.SameLine();
        ImGuiHelper.Text(about.Version.Trim());
      }

      if (!string.IsNullOrEmpty(about.ChangeLog))
      {
        ImGui.Spacing();
        ImGuiHelper.Text("Changelog:");
        var split = about.ChangeLog.Split('\n');
        foreach (var line in split)
        {
          if (line.IsNullOrWhiteSpace())
            continue;

          ImGuiHelper.Text(line);
        }
      }

      if (!string.IsNullOrEmpty(about.Description))
      {
        ImGui.Spacing();
        ImGuiHelper.Text("Description:");
        var split = about.Description.Split('\n');
        foreach (var line in split)
        {
          if (line.IsNullOrWhiteSpace())
            continue;

          ImGuiHelper.Text(line);
        }
      }

      if (about.Tags != null && about.Tags.Count > 0)
      {
        ImGui.Spacing();
        ImGuiHelper.Text("Tags:");
        foreach (var tag in about.Tags)
        {
          ImGui.Spacing();
          ImGuiHelper.Text($"\t{tag.Trim()}");
        }
      }

      if (about.DependsOn != null && about.DependsOn.Count > 0)
      {
        ImGui.Spacing();
        ImGuiHelper.Text("Depends On:");
        foreach (var modRef in about.DependsOn)
        {
          ImGui.Spacing();
          ImGuiHelper.Text($"\t{modRef}");
        }
      }

      if (about.OrderBefore != null && about.OrderBefore.Count > 0)
      {
        ImGui.Spacing();
        ImGuiHelper.Text("Order Before:");
        foreach (var modRef in about.OrderBefore)
        {
          ImGui.Spacing();
          ImGuiHelper.Text($"\t{modRef}");
        }
      }

      if (about.OrderAfter != null && about.OrderAfter.Count > 0)
      {
        ImGui.Spacing();
        ImGuiHelper.Text("Order After:");
        foreach (var modRef in about.OrderAfter)
        {
          ImGui.Spacing();
          ImGuiHelper.Text($"\t{modRef}");
        }
      }

      ImGui.Spacing();
      ImGuiHelper.Text("Assemblies:");
      if (selectedInfo != null && selectedInfo.Assemblies.Count > 0)
      {
        foreach (var assembly in selectedInfo.Assemblies)
        {
          ImGui.Spacing();
          ImGuiHelper.Text($"\t{assembly}");
        }
      }
      else
      {
        ImGuiHelper.Text($"No assemblies found.");
      }
    }
  }
}
