using BepInEx.Configuration;
using ImGuiNET;
using StationeersLaunchPad.Loading;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using UI.ImGuiUi;
using UnityEngine;

namespace StationeersLaunchPad.UI
{
  internal static class ConfigPanel
  {
    private const ImGuiWindowFlags StaticWindowFlags = 0
      | ImGuiWindowFlags.NoMove
      | ImGuiWindowFlags.NoResize
      | ImGuiWindowFlags.NoSavedSettings;

    static Dictionary<ConfigEntryBase, object> requireRestartOriginalValues = new();

    public static void DrawWorkshopConfig(ModInfo modInfo)
    {
      var screenSize = ImguiHelper.ScreenSize;
      var padding = new Vector2(25, 25);
      var topLeft = new Vector2(screenSize.x - 800f - padding.x, padding.y);
      var bottomRight = screenSize - padding;

      ImGuiHelper.Draw(() =>
      {
        ImGui.SetNextWindowSize(bottomRight - topLeft);
        ImGui.SetNextWindowPos(topLeft);
        ImGui.Begin("Mod Configuration##menuconfig", StaticWindowFlags);
        DrawConfigEditor(
          ModLoader.LoadedMods.FirstOrDefault(mod => mod.Info == modInfo),
          modInfo);
        ImGui.End();
      });
    }

    public static void DrawSettingsWindow()
    {
      var screenSize = ImguiHelper.ScreenSize;
      var padding = new Vector2(25, 25);
      var topLeft = new Vector2(screenSize.x - 800f - padding.x, padding.y);
      var bottomRight = screenSize - padding;

      ImGuiHelper.Draw(() =>
      {
        ImGui.SetNextWindowSize(bottomRight - topLeft);
        ImGui.SetNextWindowPos(topLeft);
        ImGui.Begin("LaunchPad Configuration##menulpconfig", StaticWindowFlags);
        DrawConfigFile(Configs.Sorted, category => category != "Internal");
        ImGui.End();
      });
    }

    public static void DrawConfigEditor(LoadedMod mod, ModInfo modInfo)
    {
      if (modInfo == null || modInfo.Source == ModSourceType.Core)
      {
        ImGuiHelper.TextDisabled("Select a mod to edit configuration");
        return;
      }

      if (mod == null)
      {
        ImGuiHelper.TextDisabled("Mod was not enabled at load time.");
        return;
      }

      var configFiles = mod.GetSortedConfigs();
      if (configFiles == null || configFiles.Count == 0)
      {
        ImGuiHelper.TextDisabled($"{modInfo.Name} does not have any configuration");
        return;
      }

      if (requireRestartOriginalValues.Count > 0)
      {
        ImGuiHelper.TextColored("Changes in configuration require a restart to apply", new Color(0.863f, 0.078f, 0.235f));
      }
      else
        ImGuiHelper.TextDisabled("These configurations may require a restart to apply");
      ImGui.BeginChild("##config", ImGuiWindowFlags.HorizontalScrollbar);
      foreach (var configFile in configFiles)
      {
        DrawConfigFile(configFile);
      }
      ImGui.EndChild();
    }

    public static bool DrawConfigFile(SortedConfigFile configFile, Func<string, bool> categoryFilter = null)
    {
      ImGuiHelper.Text(configFile.FileName);
      ImGui.PushID(configFile.FileName);

      var changed = false;

      foreach (var category in configFile.Categories)
      {
        if (categoryFilter != null && !categoryFilter(category.Category))
          continue;

        if (!ImGui.CollapsingHeader(category.Category, ImGuiTreeNodeFlags.DefaultOpen))
          continue;

        ImGui.PushID(category.Category);
        foreach (var entry in category.Entries)
        {
          if (entry.Visible && DrawConfigEntry(entry))
            changed = true;
        }
        ImGui.PopID();
      }

      ImGui.PopID();

      return changed;
    }

    private delegate bool DrawConfigEntryFunc(ConfigEntryWrapper wrapper, bool fill);
    private static readonly Dictionary<Type, DrawConfigEntryFunc> drawFuncs = new();
    public static bool DrawConfigEntry(ConfigEntryWrapper wrapper, bool fill = true)
    {
      var type = wrapper.Entry.SettingType;
      if (!drawFuncs.TryGetValue(type, out var draw))
      {
        var lambda = (Expression<DrawConfigEntryFunc>) ((w, f) => DrawConfigEntry<object>(w, f));
        var gmethod = (lambda.Body as MethodCallExpression).Method.GetGenericMethodDefinition();
        var method = gmethod.MakeGenericMethod(type);
        drawFuncs[type] = draw = (DrawConfigEntryFunc) method.CreateDelegate(typeof(DrawConfigEntryFunc));
      }
      return draw(wrapper, fill);
    }

    private static bool DrawConfigEntry<T>(ConfigEntryWrapper wrapper, bool fill)
    {
      ImGui.PushID(wrapper.Definition.Key);
      ImGui.BeginGroup();

      ImGui.BeginDisabled(wrapper.Disabled);
      var entry = wrapper.Entry as ConfigEntry<T>;
      var value = entry.Value;
      if (value is not bool)
      {
        if (Configs.CompactConfigPanel.Value)
          ImGui.AlignTextToFramePadding();
        ImGuiHelper.Text(wrapper.DisplayName);
        if (Configs.CompactConfigPanel.Value)
          ImGui.SameLine();
      }
      if (fill)
        ImGui.SetNextItemWidth(-float.Epsilon);

      var changed = false;
      if (wrapper.CustomDrawer != null)
      {
        try
        {
          changed = wrapper.CustomDrawer(wrapper.Entry);
        }
        catch (Exception ex)
        {
          // Modern ImGUI supports ErrorRecoveryStoreState/ErrorRecoveryTryToRecoverState, but it's not implemeneted in ImGuiNET
          // and not yet implemented in the version the game uses (1.88).
          // If it would be updated/replaced, error recovery might be possible, currently this try .. catch block is a homeopathy.
          Logger.Global.LogException(ex);
        }
      }
      else
      {
        changed = DrawFuncs<T>.Draw(wrapper, fill);
      }
      if (wrapper.RequireRestart && changed)
      {
        var newValue = entry.Value;
        // Check if the value has actually changed - in case custom drawer returns true when there was no change.
        if (!Equals(value, newValue))
        {
          if (requireRestartOriginalValues.TryGetValue(wrapper.Entry, out var originalValue))
          {
            if (Equals(newValue, originalValue))
              requireRestartOriginalValues.Remove(wrapper.Entry);
          }
          else
            requireRestartOriginalValues.Add(wrapper.Entry, value);
        }
      }

      ImGui.EndDisabled();
      ImGui.EndGroup();
      ImGui.PopID();

      var description = wrapper.Description?.Description;
      if (!string.IsNullOrEmpty(description))
        ImGuiHelper.ItemTooltip(description, 600f);
      return changed;
    }

    private static bool Equals<T>(T val, object other)
    {
      if (val == null)
        return other == null;
      return val.Equals(other);
    }

    static ConfigPanel()
    {
      AddDrawFunc<Color>(DrawColorEntry);
      AddDrawFunc<Vector2>(DrawVector2Entry);
      AddDrawFunc<Vector3>(DrawVector3Entry);
      AddDrawFunc<Vector4>(DrawVector4Entry);
      AddDrawFunc<string>(DrawStringEntry);
      AddDrawFunc<char>(DrawCharEntry);
      AddDrawFunc<bool>(DrawBoolEntry);
      AddDrawFunc<float>(DrawFloatEntry);
      AddDrawFunc<double>(DrawDoubleEntry);
      AddDrawFunc<decimal>(DrawDecimalEntry);
      AddDrawFunc<byte>(DrawByteEntry);
      AddDrawFunc<sbyte>(DrawSByteEntry);
      AddDrawFunc<short>(DrawShortEntry);
      AddDrawFunc<ushort>(DrawUShortEntry);
      AddDrawFunc<int>(DrawIntEntry);
      AddDrawFunc<uint>(DrawUIntEntry);
      AddDrawFunc<long>(DrawLongEntry);
      AddDrawFunc<ulong>(DrawULongEntry);
    }
    private static void AddDrawFunc<T>(DrawFuncs<T>.DrawFunc fn) => DrawFuncs<T>.Fn = fn;
    private static class DrawFuncs<T>
    {
      public delegate bool DrawFunc(ConfigEntry<T> entry, ConfigEntryWrapper wrapper, bool fill);
      public static DrawFunc Fn;
      public static bool Draw(ConfigEntryWrapper wrapper, bool fill)
      {
        EnsureFn();
        return Fn(wrapper.Entry as ConfigEntry<T>, wrapper, fill);
      }

      private enum DummyEnum { }
      private static void EnsureFn()
      {
        if (Fn != null)
          return;
        if (typeof(Enum).IsAssignableFrom(typeof(T)))
          SetFn<DummyEnum>((e, w, f) => DrawEnumEntry(e, w, f));
        else
          SetFn<T>((e, w, f) => DrawDefault(e, w, f));
      }

      private static void SetFn<T2>(Expression<DrawFuncs<T2>.DrawFunc> lambda)
      {
        var gmethod = (lambda.Body as MethodCallExpression).Method.GetGenericMethodDefinition();
        var method = gmethod.MakeGenericMethod(typeof(T));
        Fn = (DrawFunc) method.CreateDelegate(typeof(DrawFunc));
      }
    }

    private static bool DrawColorEntry(ConfigEntry<Color> entry, ConfigEntryWrapper wrapper, bool fill)
    {
      var value = entry.Value;
      ImGui.Spacing();
      var vector4 = new Vector4(value.r, value.g, value.b, value.a);
      if (ImGui.ColorEdit4("##color", ref vector4))
      {
        entry.Value = new Color(vector4.x, vector4.y, vector4.z, vector4.w);
        return true;
      }
      return false;
    }

    public static bool DrawEnumEntry<T>(ConfigEntry<T> entry, ConfigEntryWrapper wrapper, bool fill) where T : unmanaged, Enum
    {
      var changed = false;
      var value = entry.Value;
      var currentValue = Cast<T>.To<ulong>(value);
      var previewValue = EnumInfo<T>.FormatValue(value);

      if (!fill)
      {
        var previewSize = ImGui.CalcTextSize(previewValue);
        var style = ImGui.GetStyle();
        var fullWidth = previewSize.x + ImGui.GetFrameHeight() + style.FramePadding.x * 2;
        ImGui.SetNextItemWidth(Math.Min(fullWidth, ImGui.GetContentRegionAvail().x));
      }
      if (ImGui.BeginCombo("##enumvalue", previewValue))
      {
        var values = EnumInfo<T>.Values;
        var isFlags = EnumInfo<T>.IsFlags;
        for (var i = 0; i < values.Length; i++)
        {
          ImGui.PushID(i);

          var item = values[i];
          var itemVal = item.UlongValue;
          if (isFlags)
          {
            var isChecked = itemVal == 0
              ? currentValue == itemVal
              : (currentValue & itemVal) == itemVal;
            if (ImGui.Checkbox(item.DisplayName, ref isChecked))
            {
              entry.Value = Cast<ulong>.To<T>(isChecked
                ? currentValue | itemVal
                : currentValue & ~itemVal);
              changed = true;
            }
          }
          else
          {
            var selected = currentValue == itemVal;
            if (selected)
              ImGui.SetItemDefaultFocus();
            if (ImGui.Selectable(item.DisplayName, selected))
            {
              entry.Value = Cast<ulong>.To<T>(item.UlongValue);
              changed = true;
            }
          }

          ImGui.PopID();
        }
        ImGui.EndCombo();
      }

      return changed;
    }

    public static bool DrawStringEntry(ConfigEntry<string> entry, ConfigEntryWrapper wrapper, bool fill)
    {
      var changed = false;
      var value = entry.Value;
      if (ImGui.InputText("##stringvalue", ref value, 512, ImGuiInputTextFlags.EnterReturnsTrue) || ImGui.IsItemDeactivatedAfterEdit())
      {
        entry.Value = value;
        changed = true;
      }

      return changed;
    }

    private static readonly Dictionary<char, string> charStrings = new();
    public static bool DrawCharEntry(ConfigEntry<char> entry, ConfigEntryWrapper wrapper, bool fill)
    {
      var value = entry.Value;
      if (!charStrings.TryGetValue(value, out var str))
        str = charStrings[value] = value.ToString();

      var changed = false;
      if (ImGui.InputText("##charvalue", ref str, 1, ImGuiInputTextFlags.EnterReturnsTrue) || ImGui.IsItemDeactivatedAfterEdit())
      {
        entry.Value = str.Length > 0 ? str[0] : default;
        changed = true;
      }

      return changed;
    }

    public static bool DrawBoolEntry(ConfigEntry<bool> entry, ConfigEntryWrapper wrapper, bool fill)
    {
      var changed = false;

      var value = entry.Value;
      var compact = Configs.CompactConfigPanel.Value;
      if (compact)
      {
        ImGuiHelper.Text(wrapper.DisplayName);
        ImGui.SameLine();
      }
      if (ImGui.Checkbox(compact ? "##boolvalue" : wrapper.DisplayName, ref value))
      {
        entry.Value = value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawDecimalEntry(ConfigEntry<decimal> entry, ConfigEntryWrapper wrapper, bool fill)
    {
      (double, double)? range = null;
      if (entry.Description.AcceptableValues is AcceptableValueRange<decimal> valueRange)
        range = ((double) valueRange.MinValue, (double) valueRange.MaxValue);
      var value = (double) entry.Value;
      if (DrawScalarEntry(ref value, "##decimalvalue", ImGuiDataType.Double, wrapper.Format ?? "%.3f", range))
      {
        entry.Value = (decimal) value;
        return true;
      }
      return false;
    }

    private static bool DrawVector2Entry(ConfigEntry<Vector2> entry, ConfigEntryWrapper wrapper, bool fill) =>
      DrawScalarNEntry(entry, "##vector2", 2, ImGuiDataType.Float, wrapper.Format ?? "%.3f");

    private static bool DrawVector3Entry(ConfigEntry<Vector3> entry, ConfigEntryWrapper wrapper, bool fill) =>
      DrawScalarNEntry(entry, "##vector3", 3, ImGuiDataType.Float, wrapper.Format ?? "%.3f");

    private static bool DrawVector4Entry(ConfigEntry<Vector4> entry, ConfigEntryWrapper wrapper, bool fill) =>
      DrawScalarNEntry(entry, "##vector4", 4, ImGuiDataType.Float, wrapper.Format ?? "%.3f");

    public static bool DrawFloatEntry(ConfigEntry<float> entry, ConfigEntryWrapper wrapper, bool fill) =>
      DrawScalarEntry(entry, "##floatvalue", ImGuiDataType.Float, wrapper.Format ?? "%.3f");

    public static bool DrawDoubleEntry(ConfigEntry<double> entry, ConfigEntryWrapper wrapper, bool fill) =>
      DrawScalarEntry(entry, "##doublevalue", ImGuiDataType.Double, wrapper.Format ?? "%.3f");

    public static bool DrawByteEntry(ConfigEntry<byte> entry, ConfigEntryWrapper wrapper, bool fill) =>
      DrawScalarEntry(entry, "##bytevalue", ImGuiDataType.U8, wrapper.Format);

    public static bool DrawSByteEntry(ConfigEntry<sbyte> entry, ConfigEntryWrapper wrapper, bool fill) =>
      DrawScalarEntry(entry, "##sbytevalue", ImGuiDataType.S8, wrapper.Format);

    public static bool DrawShortEntry(ConfigEntry<short> entry, ConfigEntryWrapper wrapper, bool fill) =>
      DrawScalarEntry(entry, "##shortvalue", ImGuiDataType.S16, wrapper.Format);

    public static bool DrawUShortEntry(ConfigEntry<ushort> entry, ConfigEntryWrapper wrapper, bool fill) =>
      DrawScalarEntry(entry, "##ushortvalue", ImGuiDataType.U16, wrapper.Format);

    public static bool DrawIntEntry(ConfigEntry<int> entry, ConfigEntryWrapper wrapper, bool fill) =>
      DrawScalarEntry(entry, "##intvalue", ImGuiDataType.S32, wrapper.Format);

    public static bool DrawUIntEntry(ConfigEntry<uint> entry, ConfigEntryWrapper wrapper, bool fill) =>
      DrawScalarEntry(entry, "##uintvalue", ImGuiDataType.U32, wrapper.Format);

    public static bool DrawLongEntry(ConfigEntry<long> entry, ConfigEntryWrapper wrapper, bool fill) =>
      DrawScalarEntry(entry, "##longvalue", ImGuiDataType.S64, wrapper.Format);

    public static bool DrawULongEntry(ConfigEntry<ulong> entry, ConfigEntryWrapper wrapper, bool fill) =>
      DrawScalarEntry(entry, "##ulongvalue", ImGuiDataType.U64, wrapper.Format);

    private static bool DrawScalarEntry<T>(ConfigEntry<T> entry, string label, ImGuiDataType dataType, string format)
      where T : unmanaged, IComparable
    {
      (T, T)? range = null;
      if (entry.Description.AcceptableValues is AcceptableValueRange<T> valueRange)
        range = (valueRange.MinValue, valueRange.MaxValue);

      var value = entry.Value;
      if (DrawScalarEntry(ref value, label, dataType, format, range))
      {
        entry.Value = value;
        return true;
      }
      return false;
    }

    private static unsafe bool DrawScalarEntry<T>(
      ref T curValue, string label, ImGuiDataType dataType, string format, (T Min, T Max)? range
    ) where T : unmanaged
    {
      var changed = false;

      var value = curValue;
      if (range.HasValue)
      {
        var min = range.Value.Min;
        var max = range.Value.Max;
        if (ImGui.SliderScalar(label, dataType, (IntPtr) (&value), (IntPtr) (&min), (IntPtr) (&max), format))
        {
          curValue = value;
          changed = true;
        }
      }
      else if (ImGui.InputScalar(label, dataType, (IntPtr) (&value), format))
      {
        curValue = value;
        changed = true;
      }

      return changed;
    }

    private static unsafe bool DrawScalarNEntry<T>(ConfigEntry<T> entry, string label, int n, ImGuiDataType dataType, string format)
      where T : unmanaged
    {
      var value = entry.Value;
      ImGui.Spacing();
      if (ImGui.DragScalarN(label, dataType, (IntPtr) (&value), n, format))
      {
        entry.Value = value;
        return true;
      }
      return false;
    }

    public static bool DrawDefault<T>(ConfigEntry<T> entry, ConfigEntryWrapper wrapper, bool fill)
    {
      var value = entry.Value;
      if (value != null)
        ImGuiHelper.TextDisabled($"{value}");
      else
        ImGuiHelper.TextDisabled("is null");

      return false;
    }
  }
}
