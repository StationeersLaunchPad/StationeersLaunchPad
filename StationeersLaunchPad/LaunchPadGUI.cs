using System;
using System.Text;
using Assets.Scripts;
using BepInEx.Configuration;
using ImGuiNET;
using UI.ImGuiUi;
using UnityEngine;

namespace StationeersLaunchPad
{
  public static class LaunchPadGUI
  {
    public static bool IsActive = true;

    public static void DrawPreload()
    {
      if (GameManager.IsBatchMode)
        return;
      PushDefaultStyle();

      if (LaunchPadConfig.AutoLoad)
        DrawAutoLoad();
      else
        DrawManualLoad();

      PopDefaultStyle();
    }

    public static void DrawMenuConfig(ModData modData)
    {
      if (modData == null)
        Selected = null;
      else if (Selected == null || Selected.Path != modData.DirectoryPath)
        Selected = LaunchPadConfig.Mods.Find(mod => mod.Path == modData.DirectoryPath);

      var screenSize = ImguiHelper.ScreenSize;
      var padding = new Vector2(25, 25);
      var topLeft = new Vector2(screenSize.x - 800f - padding.x, padding.y);
      var bottomRight = screenSize - padding;

      PushDefaultStyle();

      ImGui.SetNextWindowSize(bottomRight - topLeft);
      ImGui.SetNextWindowPos(topLeft);
      if (ImGui.Begin("Mod Configuration##menuconfig", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings))
        DrawConfig();
      ImGui.End();

      PopDefaultStyle();
    }

    private static void DrawAutoLoad()
    {
      var screenSize = ImguiHelper.ScreenSize;
      var padding = new Vector2(25, 25);
      var topLeft = new Vector2(padding.x, screenSize.y - 100f - padding.y);
      var bottomRight = screenSize - padding;

      ImGui.SetNextWindowSize(bottomRight - topLeft);
      ImGui.SetNextWindowPos(topLeft);
      ImGui.Begin("##preloaderauto", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);

      var autoTime = (int)Math.Ceiling(LaunchPadConfig.AutoLoadWaitTime.Value - LaunchPadConfig.AutoStopwatch.Elapsed.TotalSeconds);

      Text("LaunchPad " + (LaunchPadConfig.LoadState switch
      {
        LoadState.Initializing => "Initializing",
        LoadState.Updating => "Updating",
        LoadState.Configuring => $"Loading mods in {autoTime}",
        LoadState.ModsLoading => "Loading mods",
        LoadState.ModsLoaded => $"Starting game in {autoTime}",
        _ => "",
      }));

      var logCount = Logger.Global.Lines.Count;
      if (logCount > 0)
        DrawLogLine(Logger.Global.Lines[logCount - 1]);
      else
        Text("");

      TextDisabled("click to configure");

      if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        LaunchPadConfig.AutoLoad = false;

      ImGui.End();
    }

    private static Vector4 LoadStateColor(LoadState expected) => LaunchPadConfig.LoadState == expected ? TextColor : TextDisabledColor;
    private static Vector4 LoadStateColor(LoadState expected1, LoadState expected2) =>
      LaunchPadConfig.LoadState == expected1 || LaunchPadConfig.LoadState == expected2 ? TextColor : TextDisabledColor;

    private static bool ConfigChanged = false;

    private static void DrawManualLoad()
    {
      var screenSize = ImguiHelper.ScreenSize;
      var padding = new Vector2(25, 25);
      var topLeft = new Vector2(padding.x, screenSize.y * 0.4f);
      var bottomRight = screenSize - padding;

      ImGui.SetNextWindowSize(bottomRight - topLeft);
      ImGui.SetNextWindowPos(topLeft);
      ImGui.Begin("##preloadermanual", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);

      ImGui.BeginColumns("##columns", 2, ImGuiOldColumnFlags.NoResize | ImGuiOldColumnFlags.GrowParentContentsSize);

      ImGui.BeginChild("##left");

      TextColored(LoadStateColor(LoadState.Initializing), "Initialize");
      ImGui.SameLine(); TextDisabled(">"); ImGui.SameLine();
      TextColored(LoadStateColor(LoadState.Configuring), "Setup Mods");
      ImGui.SameLine(); TextDisabled(">"); ImGui.SameLine();
      if (LaunchPadConfig.LoadState == LoadState.Configuring)
      {
        if (ImGui.SmallButton("Load Mods"))
          LaunchPadConfig.LoadState = LoadState.ModsLoading;
      }
      else
        TextColored(LoadStateColor(LoadState.ModsLoading, LoadState.ModsLoaded), "Load Mods");
      ImGui.SameLine(); TextDisabled(">"); ImGui.SameLine();
      if (LaunchPadConfig.LoadState == LoadState.ModsLoaded)
      {
        if (ImGui.SmallButton("Start Game"))
          LaunchPadConfig.LoadState = LoadState.GameRunning;
      }
      else
        TextDisabled("Start Game");

      switch (LaunchPadConfig.LoadState)
      {
        case LoadState.Initializing:
        case LoadState.Updating:
        case LoadState.Configuring:
          {
            ConfigChanged = false;

            if (LaunchPadConfig.LoadState == LoadState.Initializing)
            {
              Text(""); ImGui.Spacing();
            }
            else
            {
              if (DrawConfigEntry(LaunchPadConfig.AutoSort))
                ConfigChanged = true;
            }
            ImGui.Separator();

            DrawConfigTable(edit: LaunchPadConfig.LoadState == LoadState.Configuring);

            if (ConfigChanged)
            {
              if (LaunchPadConfig.AutoSort.Value)
                LaunchPadConfig.SortByDeps();
              LaunchPadConfig.SaveConfig();
            }
            break;
          }
        case LoadState.ModsLoading:
        case LoadState.ModsLoaded:
          {
            DrawLoadTable();
            break;
          }
      }

      ImGui.EndChild();

      ImGui.NextColumn();
      if (ImGui.BeginTabBar("##righttabs"))
      {
        if (ImGui.BeginTabItem("Logs"))
        {
          DrawLogs();
          ImGui.EndTabItem();
        }

        var configDisabled = LaunchPadConfig.LoadState <= LoadState.ModsLoading;
        ImGui.BeginDisabled(configDisabled);
        var open = ImGui.BeginTabItem("Mod Configuration");
        ImGui.EndDisabled();
        ItemTooltip(
          configDisabled ? "Mods must be loaded to edit configuration" : "Edit mod specific configuration",
          hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled
        );
        if (open)
        {
          DrawConfig();
          ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("LaunchPad Configuration"))
        {
          DrawExportButton();
          DrawConfigFile(LaunchPadConfig.SortedConfig);
          ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
      }
      ImGui.EndColumns();
      ImGui.End();
    }

    private static void DrawExportButton()
    {
      if (ImGui.Button("Export Mod Package"))
        LaunchPadConfig.ExportModPackage();
      ItemTooltip("Package enabled mods into a zip file for dedicated servers");
    }

    private static ModInfo DraggingMod = null;
    private static void DrawConfigTable(bool edit = false)
    {
      var mods = LaunchPadConfig.Mods;
      var hoverIndex = -1;

      if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        DraggingMod = null;

      var draggingIndex = -1;
      if (DraggingMod != null)
        draggingIndex = LaunchPadConfig.Mods.IndexOf(DraggingMod);

      if (!edit)
        ImGui.BeginDisabled();

      if (ImGui.BeginTable("##configtable", 3, ImGuiTableFlags.SizingFixedFit))
      {
        ImGui.TableSetupColumn("##enabled");
        ImGui.TableSetupColumn("##type");
        ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthStretch);
        for (var i = 0; i < mods.Count; i++)
        {
          ImGui.TableNextRow();

          ImGui.PushID(i);

          var mod = mods[i];

          ImGui.TableNextColumn();
          if (ImGui.Checkbox("##enabled", ref mod.Enabled))
            ConfigChanged = true;

          ImGui.TableNextColumn();
          ImGui.Selectable($"##rowdrag", mod == DraggingMod, ImGuiSelectableFlags.SpanAllColumns);
          if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
          {
            hoverIndex = i;
            if (DraggingMod == null && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
              DraggingMod = mod;
              draggingIndex = i;
            }
          }
          ImGui.SameLine();
          Text($"{mod.Source}");

          ImGui.TableNextColumn();
          Text(mod.DisplayName);

          if (DraggingMod != null)
          {
            if (mod.SortBefore(DraggingMod))
            {
              ImGui.SameLine();
              TextRight("Before", TextDisabledColor);
            }
            else if (DraggingMod.SortBefore(mod))
            {
              ImGui.SameLine();
              TextRight("After", TextDisabledColor);
            }
          }

          ImGui.PopID();
        }
        ImGui.EndTable();
      }

      if (!edit)
        ImGui.EndDisabled();

      if (draggingIndex != -1 && hoverIndex != -1 && draggingIndex != hoverIndex)
      {
        while (draggingIndex < hoverIndex)
        {
          (mods[draggingIndex + 1], mods[draggingIndex]) = (mods[draggingIndex], mods[draggingIndex + 1]);
          draggingIndex++;
        }
        while (draggingIndex > hoverIndex)
        {
          (mods[draggingIndex - 1], mods[draggingIndex]) = (mods[draggingIndex], mods[draggingIndex - 1]);
          draggingIndex--;
        }
        ConfigChanged = true;
      }
    }

    private static void DrawLoadTable()
    {
      var mods = LaunchPadConfig.Mods;

      if (ImGui.BeginTable("##loadtable", 3, ImGuiTableFlags.SizingFixedFit))
      {
        ImGui.TableSetupColumn("##state", 30f);
        ImGui.TableSetupColumn("##type");
        ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthStretch);
        for (var i = 0; i < mods.Count; i++)
        {
          var mod = mods[i];
          if (!mod.Enabled)
            continue;

          ImGui.PushID(i);

          ImGui.TableNextRow();
          ImGui.TableNextColumn();

          if (mod.Loaded == null)
            Text("");
          else if (mod.Loaded.LoadFailed)
            TextColored(Red, "X");
          else if (!mod.Loaded.LoadFinished)
            Text("...");
          else
            TextColored(Green, "+");

          ImGui.TableNextColumn();
          var isSelected = Selected == mod;
          if (ImGui.Selectable("##scopeselect", isSelected, ImGuiSelectableFlags.SpanAllColumns))
          {
            if (isSelected)
              Selected = null;
            else
              Selected = mod;
            ScrollLogsToEnd = true;
          }
          ImGui.SameLine();
          Text(mod.Source.ToString());

          ImGui.TableNextColumn();
          Text(mod.DisplayName);

          ImGui.PopID();
        }
        ImGui.EndTable();
      }
    }

    private static ModInfo Selected = null;
    private static bool ScrollLogsToEnd = false;
    private static int LastLogCount = 0;
    private static void DrawLogs()
    {
      ImGui.BeginChild("##logs", ImGuiWindowFlags.HorizontalScrollbar);

      var logger = Selected?.Loaded?.Logger ?? Logger.Global;

      var logLines = logger.Lines;
      var totalCount = logLines.TotalCount;
      if (totalCount != LastLogCount)
      {
        LastLogCount = totalCount;
        ScrollLogsToEnd = true;
      }
      var lineCount = logLines.Count;
      for (var i = 0; i < lineCount; i++)
        DrawLogLine(logLines[i]);

      if (ScrollLogsToEnd)
      {
        ScrollLogsToEnd = false;
        ImGui.SetScrollHereY();
      }

      if (ImGui.IsWindowHovered())
      {
        TooltipText("Click to copy logs");
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
          var logText = new StringBuilder();
          for (var i = 0; i < lineCount; i++)
            logText.AppendLine(logLines[i].Text);
          ImGui.SetClipboardText(logText.ToString());
          logger.Log("Logs copied to clipboard");
        }
      }

      ImGui.EndChild();
    }

    private static void DrawLogLine(LogLine line)
    {
      if (line.Type == LogType.Log)
        Text(line.Text);
      else if (line.Type == LogType.Warning)
        TextColored(Yellow, line.Text);
      else
        TextColored(Red, line.Text);
    }

    private static void DrawConfig()
    {
      if (Selected == null || Selected.Source == ModSource.Core)
      {
        TextDisabled("Select a mod to edit configuration");
        return;
      }
      if (Selected.Loaded == null)
      {
        TextDisabled("Mod was not enabled at load time. Restart required in order to configure");
        return;
      }
      var configFiles = Selected.Loaded?.GetSortedConfigs();
      if (configFiles == null || configFiles.Count == 0)
      {
        TextDisabled($"{Selected.DisplayName} does not have any configuration");
        return;
      }

      ImGui.BeginChild("##config", ImGuiWindowFlags.HorizontalScrollbar);
      TextDisabled("These configurations may require a restart to apply");
      foreach (var configFile in configFiles)
      {
        DrawConfigFile(configFile);
      }
      ImGui.EndChild();
    }

    private static void DrawConfigFile(SortedConfigFile configFile)
    {
      Text(configFile.FileName);
      ImGui.PushID(configFile.FileName);
      foreach (var category in configFile.Categories)
      {
        if (!ImGui.CollapsingHeader(category.Category, ImGuiTreeNodeFlags.DefaultOpen))
          continue;
        foreach (var entry in category.Entries)
        {
          DrawConfigEntry(entry);
        }
      }
      ImGui.PopID();
    }

    private unsafe static bool DrawConfigEntry(ConfigEntryBase entry, bool fill = true)
    {
      // TODO: support AcceptableValueRange and AcceptableValueList for all data types
      // This unfortunately requires a separate implementation for each type

      var changed = false;
      ImGui.PushID(entry.Definition.Key);
      ImGui.BeginGroup();

      Text(entry.Definition.Key);
      ImGui.SameLine();
      if (fill)
        ImGui.SetNextItemWidth(-float.Epsilon);
      var value = entry.BoxedValue;
      if (value is Enum enumValue)
      {
        var type = enumValue.GetType();
        var values = Enum.GetValues(type);
        var names = Enum.GetNames(type);
        var index = -1;
        for (var i = 0; i < values.Length; i++)
        {
          if (values.GetValue(i).Equals(enumValue))
          {
            index = i;
            break;
          }
        }
        if (ImGui.Combo("##enumValue", ref index, names, names.Length))
        {
          Logger.Global.Log($"{enumValue} {enumValue.GetType()} {values.GetValue(index)} {values.GetValue(index).GetType()}");
          entry.BoxedValue = values.GetValue(index);
          changed = true;
        }
      }
      else if (value is string stringValue)
      {
        if (ImGui.InputText("##stringvalue", ref stringValue, 256))
        {
          entry.BoxedValue = stringValue;
          changed = true;
        }
      }
      else if (value is bool boolValue)
      {
        if (ImGui.Checkbox("##boolvalue", ref boolValue))
        {
          entry.BoxedValue = boolValue;
          changed = true;
        }
      }
      else if (value is int intValue)
      {
        if (entry.Description.AcceptableValues is AcceptableValueRange<int> valueRange)
        {
          if (ImGui.SliderInt("##intvalue", ref intValue, valueRange.MinValue, valueRange.MaxValue))
          {
            entry.BoxedValue = intValue;
            changed = true;
          }
        }
        else if (ImGui.InputInt("##intvalue", ref intValue, step: 0))
        {
          entry.BoxedValue = intValue;
          changed = true;
        }
      }
      else if (value is float floatValue)
      {
        if (ImGui.InputFloat("##floatvalue", ref floatValue))
        {
          entry.BoxedValue = floatValue;
          changed = true;
        }
      }
      else if (value is double doubleValue)
      {
        if (ImGui.InputDouble("##doublevalue", ref doubleValue))
        {
          entry.BoxedValue = doubleValue;
          changed = true;
        }
      }
      else
      {
        Text($"{value}");
      }

      ImGui.EndGroup();
      ImGui.PopID();
      ItemTooltip(entry.Description.Description, 600f);

      return changed;
    }

    // These are helpers to always use TextUnformatted so it doesn't interpret
    // format strings and crash the game by reading garbage on the stack
    private static void Text(string text)
    {
      ImGui.TextUnformatted(text);
    }
    private static void TextColored(Vector4 color, string text)
    {
      ImGui.PushStyleColor(ImGuiCol.Text, color);
      ImGui.TextUnformatted(text);
      ImGui.PopStyleColor(1);
    }
    private static void TextDisabled(string text)
    {
      TextColored(TextDisabledColor, text);
    }
    private static void TextRight(string text, Vector4? color = null, float padding = 2)
    {
      var maxCorner = ImGui.GetContentRegionMax();
      var width = ImGui.CalcTextSize(text).x;
      ImGui.SetCursorPosX(maxCorner.x - padding - width);
      var minCorner = ImGui.GetCursorPos();
      ImGui.GetWindowDrawList().AddRectFilled(minCorner, maxCorner, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 1)));
      TextColored(color ?? TextColor, text);
    }
    private static void TooltipText(string text, float wrapWidth = float.MaxValue)
    {
      ImGui.BeginTooltip();
      ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapWidth);
      Text(text);
      ImGui.PopTextWrapPos();
      ImGui.EndTooltip();
    }
    private static void ItemTooltip(string text, float wrapWidth = float.MaxValue, ImGuiHoveredFlags hoverFlags = ImGuiHoveredFlags.None)
    {
      if (ImGui.IsItemHovered(hoverFlags))
        TooltipText(text, wrapWidth);
    }

    private static Vector4 TextColor => _colors[(int)ImGuiCol.Text];
    private static Vector4 TextDisabledColor => _colors[(int)ImGuiCol.TextDisabled];
    private static Vector4 Green => new Vector4(0, 1, 0, 1);
    private static Vector4 Red = new Vector4(1, 0, 0, 1);
    private static Vector4 Yellow = new Vector4(0.7f, 0.7f, 0, 1);
    private static Vector4[] _colors;
    private static void PushDefaultStyle()
    {
      if (_colors == null)
      {
        // initialize style colors
        _colors = new Vector4[(int)ImGuiCol.COUNT];
        for (var i = 0; i < (int)ImGuiCol.COUNT; i++)
        {
          _colors[i] = ImGui.ColorConvertU32ToFloat4(ImGui.GetColorU32((ImGuiCol)i));
          // premultiply alpha and set to opaque if not fully transparent
          _colors[i] = _colors[i] * _colors[i][3];
          if (_colors[i][3] != 0f)
            _colors[i][3] = 1f;
        }
      }

      for (var i = (ImGuiCol)0; i < ImGuiCol.COUNT; i++)
      {
        ImGui.PushStyleColor(i, _colors[(int)i]);
      }
      ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(3, 2));
      ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(3, 2));
      ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 3);
      ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 1);
    }

    private static void PopDefaultStyle()
    {
      ImGui.PopStyleVar(4);
      ImGui.PopStyleColor((int)ImGuiCol.COUNT);
    }
  }
}