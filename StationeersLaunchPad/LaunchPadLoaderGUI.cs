using ImGuiNET;
using System;
using System.Linq;
using UnityEngine;

namespace StationeersLaunchPad
{
  public class LaunchPadLoaderGUI
  {
    public static bool IsActive = false;

    public static void Draw()
    {
      if (!IsActive)
        return;

      ImGuiHelper.Draw(() => DrawLoading());
    }

    public static void DrawLoading()
    {
      if (LaunchPadConfig.AutoLoad)
        DrawAutoLoad();
      else
        DrawManualLoad();
    }

    private static int autoTime => (int) Math.Ceiling(LaunchPadConfig.AutoLoadWaitTime.Value - LaunchPadConfig.AutoStopwatch.Elapsed.TotalSeconds);
    public static void DrawAutoLoad()
    {
      ImGuiHelper.DrawWithPadding(() => ImGui.Begin("##preloaderauto", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings));

      DrawLoadingState();

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
          LaunchPadConfig.AutoLoad = false;
      });

      ImGui.End();
    }

    public static void DrawLoadingState() => ImGuiHelper.Text(LaunchPadConfig.LoadState switch
    {
      LoadState.Initializing => "LaunchPad Initializing",
      LoadState.Searching => "LaunchPad Finding Mods",
      LoadState.Updating => "LaunchPad Checking for Updating",
      LoadState.Configuring => $"LaunchPad Loading Mods in {autoTime}s",
      LoadState.Loading => "LaunchPad Loading Mods",
      LoadState.Loaded => $"Starting game in {autoTime}s",
      LoadState.Running => "Game Running",
      LoadState.Updated => "LaunchPad Updated",
      LoadState.Failed => "LaunchPad Loading Failed",
      _ => throw new ArgumentOutOfRangeException(),
    });

    public static void DrawManualLoad()
    {
      ImGuiHelper.DrawWithPadding2(() => ImGui.Begin("##preloadermanual", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings));

      ImGuiHelper.TextColored("Initalize", LoadStateColor(LoadState.Initializing));
      ImGuiHelper.ItemTooltip("State when LaunchPad is initalizing core components.");
      ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextDisabled(">"), true);

      ImGuiHelper.TextColored("Locate Mods", LoadStateColor(LoadState.Searching));
      ImGuiHelper.ItemTooltip("State when LaunchPad is searching for mods.");
      ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextDisabled(">"), true);

      if (LaunchPadConfig.CheckUpdate)
      {
        ImGuiHelper.TextColored("Check Updates", LoadStateColor(LoadState.Updating));
        ImGuiHelper.ItemTooltip("State when LaunchPad is checking for updates.");
        ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextDisabled(">"), true);
      }

      if (LaunchPadConfig.LoadState == LoadState.Configuring)
      {
        if (ImGui.SmallButton("Load Mods"))
          LaunchPadConfig.LoadState = LoadState.Loading;
      }
      else
      {
        ImGuiHelper.TextColored("Load Mods", LoadStateColor(LoadState.Loading));
      }
      ImGuiHelper.ItemTooltip("State when LaunchPad is ready to load mods.");

      ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextDisabled(">"), true);
      if (LaunchPadConfig.LoadState == LoadState.Loaded)
      {
        if (ImGui.SmallButton("Start Game"))
          LaunchPadConfig.LoadState = LoadState.Running;
      }
      else
      {
        ImGuiHelper.TextColored("Start Game", LoadStateColor(LoadState.Loaded));
      }
      ImGuiHelper.ItemTooltip("State when LaunchPad is ready to load the game.");
      ImGui.Separator();

      ImGui.BeginColumns("##columns", 2, ImGuiOldColumnFlags.NoResize | ImGuiOldColumnFlags.GrowParentContentsSize);
      ImGui.BeginChild("##left");

      switch (LaunchPadConfig.LoadState)
      {
        case LoadState.Initializing:
        case LoadState.Searching:
        case LoadState.Updating:
        case LoadState.Configuring:
        {
          LaunchPadConfigGUI.ConfigChanged = false;

          if (LaunchPadConfig.LoadState == LoadState.Initializing)
          {
            ImGuiHelper.Text("");
            ImGui.Spacing();
          }
          else
          {
            if (LaunchPadConfigGUI.DrawConfigEntry(LaunchPadConfig.AutoSortOnStart))
            {
              LaunchPadConfig.AutoSort = LaunchPadConfig.AutoSortOnStart.Value;
              LaunchPadConfigGUI.ConfigChanged = true;
            }

            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.y);
            ImGui.Separator();
          }

          LaunchPadConfigGUI.DrawConfigTable(LaunchPadConfig.LoadState == LoadState.Configuring);

          if (LaunchPadConfigGUI.ConfigChanged)
          {
            if (LaunchPadConfig.AutoSort)
              LaunchPadConfig.SortByDeps();

            LaunchPadConfig.SaveConfig();
          }
          break;
        }

        case LoadState.Loading:
        case LoadState.Loaded:
        {
          DrawLoadTable();
          break;
        }

        default:
        case LoadState.Running:
        case LoadState.Updated:
        case LoadState.Failed:
          break;
      }
      ImGui.EndChild();

      ImGui.NextColumn();
      ImGui.SetCursorPosY(ImGui.GetStyle().ItemSpacing.y * 2);
      if (ImGui.BeginTabBar("##right"))
      {
        if (ImGui.BeginTabItem("Logs"))
        {
          LaunchPadConsoleGUI.DrawConsole();
          ImGui.EndTabItem();
        }

        var disabled = LaunchPadConfig.LoadState <= LoadState.Loading;
        ImGui.BeginDisabled(disabled);
        var open = ImGui.BeginTabItem("Mod Configuration");
        ImGui.EndDisabled();
        ImGuiHelper.ItemTooltip(
          disabled ? "Mods must be loaded to edit configuration" : "Edit mod specific configuration",
          hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled
        );
        if (open)
        {
          LaunchPadConfigGUI.DrawConfigEditor();
          ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("LaunchPad Configuration"))
        {
          DrawExportButton();
          LaunchPadConfigGUI.DrawConfigFile(LaunchPadConfig.SortedConfig, (category) => category == "Internal");

          ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
      }

      ImGui.EndColumns();
      ImGui.End();
    }

    public static ModInfo SelectedInfo = null;
    public static LoadedMod SelectedMod => SelectedInfo?.Loaded;
    public static Logger SelectedLogger => SelectedMod?.Logger;
    public static void DrawLoadTable()
    {
      if (ImGui.BeginTable("##loadtable", 3, ImGuiTableFlags.SizingFixedFit))
      {
        ImGui.TableSetupColumn("##state", 30f);
        ImGui.TableSetupColumn("##type");
        ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthStretch);
        for (var i = 0; i < LaunchPadConfig.Mods.Count; i++)
        {
          var info = LaunchPadConfig.Mods[i];
          if (info == null || !info.Enabled)
            continue;

          var mod = info.Loaded;

          ImGui.PushID(i);

          ImGui.TableNextRow();
          ImGui.TableNextColumn();

          DrawModState(info);

          ImGui.TableNextColumn();
          var isSelected = SelectedInfo == info;
          if (ImGui.Selectable("##scopeselect", isSelected, ImGuiSelectableFlags.SpanAllColumns))
          {
            SelectedInfo = isSelected ? null : info;
            LaunchPadConsoleGUI.shouldScroll = LaunchPadConfig.AutoScroll;
          }
          ImGuiHelper.DrawSameLine(() => ImGuiHelper.Text($"{info.Source}"));

          ImGui.TableNextColumn();
          ImGuiHelper.Text(info.DisplayName);

          ImGui.PopID();
        }
      }
      ImGui.EndTable();
    }

    public static void DrawModState(ModInfo info)
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

    public static void DrawExportButton()
    {
      if (ImGui.Button("Export Mod Package"))
        LaunchPadConfig.ExportModPackage();
      ImGuiHelper.ItemTooltip("Package enabled mods into a zip file for dedicated servers.");
    }

    public static bool IsLoadState(params LoadState[] expected) => expected.Contains(LaunchPadConfig.LoadState);
    public static Color LoadStateColor(params LoadState[] expected) => IsLoadState(expected) ? ImGuiHelper.TextColor : ImGuiHelper.TextDisabledColor;
  }
}
