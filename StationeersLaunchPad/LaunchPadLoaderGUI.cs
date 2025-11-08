using BepInEx;
using ImGuiNET;
using System;

namespace StationeersLaunchPad
{
  public static class LaunchPadLoaderGUI
  {
    [Flags]
    public enum ChangeFlags
    {
      None = 0,
      Mods = 1 << 0,
      AutoSort = 1 << 1,
      NextStep = 1 << 2,
    }

    private static LoadState lastState;
    private static ModInfo selectedInfo = null;
    private static bool openLogs = false;
    private static bool openInfo = false;
    private static ModInfo draggingMod = null;
    private static bool dragged = false;

    // returns true if the user clicked to stop autoloading
    public static bool DrawAutoLoad(LoadState loadState, double autoTime)
    {
      var stopAuto = false;
      ImGuiHelper.Draw(() =>
      {
        ImGuiHelper.DrawWithPadding(() => ImGui.Begin("##preloaderauto", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings));

        ImGuiHelper.Text($"StationeersLaunchPad {LaunchPadInfo.VERSION}");
        ImGuiHelper.Text(loadState switch
        {
          LoadState.Updating => "Checking for Update",
          LoadState.Initializing => "Initializing",
          LoadState.Searching => "Finding Mods",
          LoadState.Configuring => $"Loading Mods in {autoTime:0.0}s",
          LoadState.Loading => "Loading Mods",
          LoadState.Loaded => $"Starting game in {autoTime:0.0}s",
          LoadState.Running => "Game Running",
          LoadState.Failed => "Loading Failed",
          _ => throw new ArgumentOutOfRangeException(),
        });

        ImGui.Spacing();
        var line = Logger.Global.Last();
        if (line != null)
          LaunchPadConsoleGUI.DrawConsoleLine(line, true);
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

    public static ChangeFlags DrawManualLoad(LoadState loadState, ModList modList)
    {
      var changed = ChangeFlags.None;

      // when we move into the loading step, clear the selected mod so all the logs are visible
      if (lastState < LoadState.Loaded && loadState >= LoadState.Loading)
        selectedInfo = null;
      lastState = loadState;

      ImGuiHelper.Draw(() =>
      {
        ImGuiHelper.DrawWithPadding2(() => ImGui.Begin("##preloadermanual", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings));

        ImGuiHelper.TextDisabled(LaunchPadInfo.VERSION);
        ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextDisabled("|"), true);

        if (Configs.CheckForUpdate.Value)
        {
          ImGuiHelper.TextDisabled("Update", loadState != LoadState.Updating);
          ImGuiHelper.ItemTooltip("Checking for updates to StationeersLaunchPad.");
          ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextDisabled(">"), true);
        }

        ImGuiHelper.TextDisabled("Initalize", loadState != LoadState.Initializing);
        ImGuiHelper.ItemTooltip("State when LaunchPad is initalizing core components.");
        ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextDisabled(">"), true);

        ImGuiHelper.TextDisabled("Locate Mods", loadState != LoadState.Searching);
        ImGuiHelper.ItemTooltip("State when LaunchPad is searching for mods.");
        ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextDisabled(">"), true);

        if (loadState == LoadState.Configuring)
        {
          if (ImGui.SmallButton("Load Mods"))
          {
            changed |= ChangeFlags.NextStep;
            openLogs = true;
          }
        }
        else
        {
          ImGuiHelper.TextDisabled("Load Mods", loadState != LoadState.Loading);
        }
        ImGuiHelper.ItemTooltip("State when LaunchPad is ready to load mods.");

        ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextDisabled(">"), true);
        if (loadState == LoadState.Loaded || loadState == LoadState.Failed)
        {
          if (ImGui.SmallButton("Start Game"))
            changed |= ChangeFlags.NextStep;
        }
        else
        {
          ImGuiHelper.TextDisabled("Start Game");
        }
        ImGuiHelper.ItemTooltip("State when LaunchPad is ready to load the game.");
        ImGui.Separator();

        ImGui.BeginColumns("##columns", 2, ImGuiOldColumnFlags.NoResize | ImGuiOldColumnFlags.GrowParentContentsSize);
        ImGui.BeginChild("##left");

        switch (loadState)
        {
          case LoadState.Initializing:
          case LoadState.Searching:
          case LoadState.Updating:
          case LoadState.Configuring:
          {
            if (loadState == LoadState.Initializing)
            {
              ImGuiHelper.Text("");
              ImGui.Spacing();
            }
            else
            {
              if (LaunchPadConfigGUI.DrawConfigEntry(Configs.AutoSortOnStart))
                changed |= ChangeFlags.AutoSort;

              ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.y);
              ImGui.Separator();
            }

            if (DrawConfigTable(modList, loadState == LoadState.Configuring))
              changed |= ChangeFlags.Mods;
            break;
          }

          case LoadState.Loading:
          case LoadState.Loaded:
          {
            DrawLoadTable(modList);
            break;
          }

          default:
          case LoadState.Running:
          case LoadState.Failed:
            break;
        }
        ImGui.EndChild();

        ImGui.NextColumn();
        ImGui.SetCursorPosY(ImGui.GetStyle().ItemSpacing.y * 2);
        if (ImGui.BeginTabBar("##right"))
        {
          if (ImGui.BeginTabItem("Logs", openLogs ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
          {
            LaunchPadConsoleGUI.DrawConsole(selectedInfo?.Loaded?.Logger ?? Logger.Global);
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
          else if (!openInfo && loadState <= LoadState.Loading)
            selectedInfo = null;
          openInfo = false;

          var disabled = loadState <= LoadState.Loading;
          ImGui.BeginDisabled(disabled);
          open = ImGui.BeginTabItem("Mod Configuration");
          ImGui.EndDisabled();
          ImGuiHelper.ItemTooltip(
            disabled ? "Mods must be loaded to edit configuration" : "Edit mod specific configuration",
            hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled
          );
          if (open)
          {
            LaunchPadConfigGUI.DrawConfigEditor(selectedInfo);
            ImGui.EndTabItem();
          }

          if (ImGui.BeginTabItem("LaunchPad Configuration"))
          {
            DrawExportButton();
            var configChanged = LaunchPadConfigGUI.DrawConfigFile(Configs.Sorted, category => category != "Internal");
            // If we changed launchpad config and haven't loaded mods yet, mark mods changed to apply disable/sort behaviour
            if (loadState <= LoadState.Configuring && configChanged)
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

    private static bool DrawConfigTable(ModList modList, bool edit = false)
    {
      var changed = false;
      if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
      {
        if (draggingMod != null && !dragged)
        {
          selectedInfo = draggingMod;
          if (selectedInfo != null && selectedInfo.Source == ModSource.Core)
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
          ImGuiHelper.Text($"{mod.DisplayName}");

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
        if (modList.MoveModTo(draggingMod, hoveringIndex))
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
          ImGuiHelper.Text(info.DisplayName);

          ImGui.PopID();
          idx++;
        }
      }
      ImGui.EndTable();
    }

    private static void DrawModState(ModInfo info)
    {
      var mod = info?.Loaded;

      if (info.Source == ModSource.Core)
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
      var workshopId = selectedInfo.Wrapped.Id;
      if (workshopId == 0)
        workshopId = about?.WorkshopHandle ?? 0;

      ImGuiHelper.Text(selectedInfo.DisplayName);

      if (ImGui.Button("Open Local Folder"))
        selectedInfo.OpenLocalFolder();

      if (workshopId > 1)
      {
        ImGui.SameLine();
        if (ImGui.Button("Open Workshop Page"))
          selectedInfo.OpenWorkshopPage();
      }

      ImGui.Spacing();
      ImGuiHelper.Text("Source:");
      ImGui.SameLine();
      ImGuiHelper.Text(selectedInfo.Source.ToString());

      if (!string.IsNullOrEmpty(selectedInfo.Path))
      {
        ImGui.Spacing();
        ImGuiHelper.Text("Path:");
        ImGui.SameLine();
        ImGuiHelper.Text(selectedInfo.Path);
      }

      if (about == null)
      {
        ImGui.Spacing();
        ImGuiHelper.TextDisabled("Missing About.xml");
        return;
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

      if (about.Dependencies != null && about.Dependencies.Count > 0)
      {
        ImGui.Spacing();
        ImGuiHelper.Text("Dependencies:");
        foreach (var dependency in about.Dependencies)
        {
          ImGui.Spacing();
          ImGuiHelper.Text($"\tId: {dependency.Id}, Version: {dependency.Version ?? "any"}");
        }
      }

      if (about.LoadBefore != null && about.LoadBefore.Count > 0)
      {
        ImGui.Spacing();
        ImGuiHelper.Text("Load Before:");
        foreach (var m in about.LoadBefore)
        {
          ImGui.Spacing();
          ImGuiHelper.Text($"\tId: {m.Id}, Version: {m.Version ?? "any"}");
        }
      }

      if (about.LoadAfter != null && about.LoadAfter.Count > 0)
      {
        ImGui.Spacing();
        ImGuiHelper.Text("Load After:");
        foreach (var m in about.LoadAfter)
        {
          ImGui.Spacing();
          ImGuiHelper.Text($"\tId: {m.Id}, Version: {m.Version ?? "any"}");
        }
      }

      ImGui.Spacing();
      ImGuiHelper.Text("Assemblies:");
      if (selectedInfo != null && selectedInfo.Assemblies.Count > 0)
      {
        foreach (var assembly in selectedInfo.Assemblies)
        {
          ImGui.Spacing();
          ImGuiHelper.Text($"\tName: {assembly.Name}");
          ImGuiHelper.Text($"\tPath: {assembly.Path}");
        }
      }
      else
      {
        ImGuiHelper.Text($"No assemblies found.");
      }
    }
  }
}
