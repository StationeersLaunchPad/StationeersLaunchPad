using BepInEx.Configuration;
using ImGuiNET;
using StationeersLaunchPad.Loading;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UI.ImGuiUi;
using UnityEngine;

namespace StationeersLaunchPad.UI
{
  internal class ConfigPanel
  {
    private const ImGuiWindowFlags StaticWindowFlags = 0
      | ImGuiWindowFlags.NoMove
      | ImGuiWindowFlags.NoResize
      | ImGuiWindowFlags.NoSavedSettings;

    static Dictionary<Type, ulong[]> enumValuesCache = new();
    static Dictionary<Type, string[]> enumNamesCache = new();
    static Dictionary<Type, string[]> enumShortNamesCache = new();
    static Dictionary<Type, (ulong Value, string Name)[]> enumCacheSorted = new();
    static Dictionary<ConfigEntryBase, object> requireRestartOriginalValues = new();
    static Dictionary<ConfigEntryBase, (ulong Value, string Formatted)> formattedValuesCache = new();

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

        foreach (var entry in category.Entries)
        {
          if(entry.Visible && DrawConfigEntry(entry))
              changed = true;
        }
      }

      ImGui.PopID();

      return changed;
    }

    public static bool DrawConfigEntry(ConfigEntryWrapper wrapper, bool fill = true)
    {
      ImGui.PushID(wrapper.Definition.Key);
      ImGui.BeginGroup();

      ImGui.BeginDisabled(wrapper.Disabled);
      var value = wrapper.BoxedValue;
      if (value is not bool)
      {
        ImGuiHelper.Text(wrapper.DisplayName);
        if (Configs.CompactConfigPanel.Value)
          ImGui.SameLine();
      }
      if (fill)
        ImGui.SetNextItemWidth(-float.Epsilon);

      bool changed = false;
      if (wrapper.CustomDrawer != null)
      {
        try
        {
          changed = wrapper.CustomDrawer(wrapper.Entry);
        }
        // Modern ImGUI supports ErrorRecoveryStoreState/ErrorRecoveryTryToRecoverState, but it's not implemeneted in ImGuiNET
        // and not yet implemented in the version the game uses (1.88).
        // If it would be updated/replaced, error recovery might be possible, currently this try .. catch block is a homeopathy.
        catch { }
      }
      else
      {
        changed = value switch
        {
          Color => DrawColorEntry(wrapper.Entry as ConfigEntry<Color>),
          Vector2 => DrawVector2Entry(wrapper.Entry as ConfigEntry<Vector2>, wrapper.Format),
          Vector3 => DrawVector3Entry(wrapper.Entry as ConfigEntry<Vector3>, wrapper.Format),
          Vector4 => DrawVector4Entry(wrapper.Entry as ConfigEntry<Vector4>, wrapper.Format),

          Enum => DrawEnumEntry(wrapper.Entry, value as Enum),
          string => DrawStringEntry(wrapper.Entry as ConfigEntry<string>),
          char => DrawCharEntry(wrapper.Entry as ConfigEntry<char>),
          bool => DrawBoolEntry(wrapper.Entry as ConfigEntry<bool>, wrapper.DisplayName),
          float => DrawFloatEntry(wrapper.Entry as ConfigEntry<float>, wrapper.Format),
          double => DrawDoubleEntry(wrapper.Entry as ConfigEntry<double>, wrapper.Format),
          decimal => DrawDecimalEntry(wrapper.Entry as ConfigEntry<decimal>, wrapper.Format),
          byte => DrawByteEntry(wrapper.Entry as ConfigEntry<byte>),
          sbyte => DrawSByteEntry(wrapper.Entry as ConfigEntry<sbyte>),
          short => DrawShortEntry(wrapper.Entry as ConfigEntry<short>),
          ushort => DrawUShortEntry(wrapper.Entry as ConfigEntry<ushort>),
          int => DrawIntEntry(wrapper.Entry as ConfigEntry<int>),
          uint => DrawUIntEntry(wrapper.Entry as ConfigEntry<uint>),
          long => DrawLongEntry(wrapper.Entry as ConfigEntry<long>),
          ulong => DrawULongEntry(wrapper.Entry as ConfigEntry<ulong>),
          _ => DrawDefault(wrapper.Entry),
        };
      }
      if (wrapper.RequireRestart && changed)
      {
        var newValue = wrapper.BoxedValue;
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

    private static bool DrawColorEntry(ConfigEntry<Color> entry)
    {
      var value = entry.Value;
      var r = value.r;
      ImGui.Spacing();
      var vector4 = new Vector4(value.r, value.g, value.b, value.a);
      if (ImGui.ColorEdit4("##color", ref vector4))
      {
        entry.BoxedValue = new Color(vector4.x, vector4.y, vector4.z, vector4.w);
        return true;
      }
      return false;
    }

    private static bool DrawVector2Entry(ConfigEntry<Vector2> entry, string format)
    {
      var value = entry.Value;
      ImGui.Spacing();
      if (ImGui.DragFloat2("##vector2", ref value, format))
      {
        entry.BoxedValue = value;
        return true;
      }
      return false;
    }

    private static bool DrawVector3Entry(ConfigEntry<Vector3> entry, string format)
    {
      var value = entry.Value;
      ImGui.Spacing();
      if (ImGui.DragFloat3("##vector3", ref value, format))
      {
        entry.BoxedValue = value;
        return true;
      }
      return false;
    }

    private static bool DrawVector4Entry(ConfigEntry<Vector4> entry, string format)
    {
      var value = entry.Value;
      ImGui.Spacing();
      if (ImGui.DragFloat4("##vector4", ref value, format))
      {
        entry.BoxedValue = value;
        return true;
      }
      return false;
    }

    public static bool DrawEnumEntry(ConfigEntryBase entry, Enum value)
    {
      var changed = false;
      var currentValue = Convert.ToUInt64(value);
      var type = value.GetType();
      var values = GetEnumValues(type);
      var names = GetEnumDisplayNames(type);
      var flags = type.GetCustomAttribute<FlagsAttribute>() != null;

      string previewValue;
      if(formattedValuesCache.TryGetValue(entry, out var formatted) && formatted.Value == currentValue)
        previewValue = formatted.Formatted;
      else
      {
        previewValue = FormatEnumValue(type, value);
        formattedValuesCache[entry] = (currentValue, previewValue);
      }
      if (ImGui.BeginCombo("##enumvalue", previewValue))
      {
        for (var i = 0; i < values.Length; i++)
        {
          var item = values[i];
          if (flags)
          {
            var isChecked = item == 0 ? currentValue == item : (currentValue & item) == item;
            if (ImGui.Checkbox(names.GetValue(i).ToString(), ref isChecked))
            {
              entry.BoxedValue = Enum.ToObject(type, isChecked ? currentValue | item : currentValue & ~item);
              changed = true;
            }
          }
          else
          {
            var selected = currentValue == values[i];
            if (selected)
              ImGui.SetItemDefaultFocus();
            if (ImGui.Selectable(names[i], selected))
            {
              entry.BoxedValue = Enum.ToObject(type, values[i]);
              changed = true;
            }
          }
        }
        ImGui.EndCombo();
      }

      return changed;
    }

    public static bool DrawStringEntry(ConfigEntry<string> entry)
    {
      var changed = false;
      var value = entry.Value;
      if (ImGui.InputText("##stringvalue", ref value, 512, ImGuiInputTextFlags.EnterReturnsTrue) || ImGui.IsItemDeactivatedAfterEdit())
      {
        entry.BoxedValue = value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawCharEntry(ConfigEntry<char> entry)
    {
      var changed = false;
      var value = $"{entry.Value}";
      if (ImGui.InputText("##charvalue", ref value, 1, ImGuiInputTextFlags.EnterReturnsTrue) || ImGui.IsItemDeactivatedAfterEdit())
      {
        entry.BoxedValue = value[0];
        changed = true;
      }

      return changed;
    }

    public static bool DrawBoolEntry(ConfigEntry<bool> entry, string displayName)
    {
      var changed = false;

      var value = entry.Value;
      var compact = Configs.CompactConfigPanel.Value;
      if (compact)
      {
        ImGuiHelper.Text(displayName);
        ImGui.SameLine();
      }
      if (ImGui.Checkbox(compact ? "##boolvalue" : displayName, ref value))
      {
        entry.BoxedValue = value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawFloatEntry(ConfigEntry<float> entry, string format)
    {
      var changed = false;

      var value = entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<float> valueRange)
      {
        if (ImGui.SliderFloat("##floatvalue", ref value, valueRange.MinValue, valueRange.MaxValue, format))
        {
          entry.BoxedValue = value;
          changed = true;
        }
      }
      else if (ImGui.InputFloat("##floatvalue", ref value, step: 0, format))
      {
        entry.BoxedValue = value;
        changed = true;
      }

      return changed;
    }

    public static unsafe bool DrawDoubleEntry(ConfigEntry<double> entry, string format)
    {
      var changed = false;

      var value = entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<double> valueRange)
      {
        var min = valueRange.MinValue;
        var max = valueRange.MaxValue;
        if (ImGui.SliderScalar("##doublevalue", ImGuiDataType.Double, (IntPtr) (&value), (IntPtr) (&min), (IntPtr) (&max), format))
        {
          entry.BoxedValue = value;
          changed = true;
        }
      }
      else if (ImGui.InputDouble("##doublevalue", ref value, step: 0, format))
      {
        entry.BoxedValue = value;
        changed = true;
      }

      return changed;
    }

    public static unsafe bool DrawDecimalEntry(ConfigEntry<decimal> entry, string format)
    {
      var changed = false;

      var value = (double) entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<decimal> valueRange)
      {
        var min = valueRange.MinValue;
        var max = valueRange.MaxValue;
        if (ImGui.SliderScalar("##decimalvalue", ImGuiDataType.Double, (IntPtr) (&value), (IntPtr) (&min), (IntPtr) (&max), format))
        {
          entry.BoxedValue = value;
          changed = true;
        }
      }
      else if (ImGui.InputDouble("##decimalvalue", ref value, step: 0, format))
      {
        entry.BoxedValue = value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawByteEntry(ConfigEntry<byte> entry)
    {
      var changed = false;

      var value = (int) entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<byte> valueRange)
      {
        if (ImGui.SliderInt("##bytevalue", ref value, valueRange.MinValue, valueRange.MaxValue))
        {
          entry.BoxedValue = (byte) value;
          changed = true;
        }
      }
      else if (ImGui.InputInt("##bytevalue", ref value, step: 0))
      {
        entry.BoxedValue = (byte) value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawSByteEntry(ConfigEntry<sbyte> entry)
    {
      var changed = false;
      var value = (int) entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<sbyte> valueRange)
      {
        if (ImGui.SliderInt("##sbytevalue", ref value, valueRange.MinValue, valueRange.MaxValue))
        {
          entry.BoxedValue = (sbyte) value;
          changed = true;
        }
      }
      else if (ImGui.InputInt("##sbytevalue", ref value, step: 0))
      {
        entry.BoxedValue = (sbyte) value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawShortEntry(ConfigEntry<short> entry)
    {
      var changed = false;

      var value = (int) entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<short> valueRange)
      {
        if (ImGui.SliderInt("##shortvalue", ref value, valueRange.MinValue, valueRange.MaxValue))
        {
          entry.BoxedValue = (short) value;
          changed = true;
        }
      }
      else if (ImGui.InputInt("##shortvalue", ref value, step: 0))
      {
        entry.BoxedValue = (short) value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawUShortEntry(ConfigEntry<ushort> entry)
    {
      var changed = false;

      var value = (int) entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<ushort> valueRange)
      {
        if (ImGui.SliderInt("##ushortvalue", ref value, valueRange.MinValue, valueRange.MaxValue))
        {
          entry.BoxedValue = (ushort) value;
          changed = true;
        }
      }
      else if (ImGui.InputInt("##ushortvalue", ref value, step: 0))
      {
        entry.BoxedValue = (ushort) value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawIntEntry(ConfigEntry<int> entry)
    {
      var changed = false;

      var value = entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<int> valueRange)
      {
        if (ImGui.SliderInt("##intvalue", ref value, valueRange.MinValue, valueRange.MaxValue))
        {
          entry.BoxedValue = value;
          changed = true;
        }
      }
      else if (ImGui.InputInt("##intvalue", ref value, step: 0))
      {
        entry.BoxedValue = value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawUIntEntry(ConfigEntry<uint> entry)
    {
      var changed = false;

      var value = (int) entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<uint> valueRange)
      {
        if (ImGui.SliderInt("##uintvalue", ref value, (int) valueRange.MinValue, (int) valueRange.MaxValue))
        {
          entry.BoxedValue = (uint) value;
          changed = true;
        }
      }
      else if (ImGui.InputInt("##uintvalue", ref value, step: 0))
      {
        entry.BoxedValue = (uint) value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawLongEntry(ConfigEntry<long> entry)
    {
      var changed = false;

      var value = (int) entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<long> valueRange)
      {
        if (ImGui.SliderInt("##longvalue", ref value, (int) valueRange.MinValue, (int) valueRange.MaxValue))
        {
          entry.BoxedValue = (long) value;
          changed = true;
        }
      }
      else if (ImGui.InputInt("##longvalue", ref value, step: 0))
      {
        entry.BoxedValue = (long) value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawULongEntry(ConfigEntry<ulong> entry)
    {
      var changed = false;

      var value = (int) entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<ulong> valueRange)
      {
        if (ImGui.SliderInt("##ulongvalue", ref value, (int) valueRange.MinValue, (int) valueRange.MaxValue))
        {
          entry.BoxedValue = (ulong) value;
          changed = true;
        }
      }
      else if (ImGui.InputInt("##ulongvalue", ref value, step: 0))
      {
        entry.BoxedValue = (ulong) value;
        changed = true;
      }

      return changed;
    }

    public static bool DrawDefault(ConfigEntryBase entry)
    {
      var changed = false;

      var value = entry.BoxedValue;
      if (value != null)
        ImGuiHelper.TextDisabled($"{value}");
      else
        ImGuiHelper.TextDisabled("is null");

      return changed;
    }

    private static ulong[] GetEnumValues(Type enumType)
    {
      if (!enumValuesCache.TryGetValue(enumType, out var values))
      {
        // Can't use GetValues because order is different from enumType.GetFields.
        var fields = enumType.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        values = new ulong[fields.Length];
        for (int i = 0; i < fields.Length; i++)
          values[i] = (ulong)Convert.ChangeType(fields[i].GetValue(null), TypeCode.UInt64);
        enumValuesCache.Add(enumType, values);
      }
      return values;
    }
    private static string[] GetEnumDisplayNames(Type enumType)
    {
      if (!enumNamesCache.TryGetValue(enumType, out var result))
      {
        var fields = enumType.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        result = new string[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
          var field = fields[i];
          var attr = field.GetCustomAttribute<System.ComponentModel.DataAnnotations.DisplayAttribute>();
          if (attr != null)
            result[i] = attr.GetName();
          else
            result[i] = field.Name;
        }
        enumNamesCache.Add(enumType, result);
      }
      return result;
    }
    private static string[] GetEnumDisplayShortNames(Type enumType)
    {
      if (!enumShortNamesCache.TryGetValue(enumType, out var result))
      {
        var fields = enumType.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        result = new string[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
          var field = fields[i];
          var attr = field.GetCustomAttribute<System.ComponentModel.DataAnnotations.DisplayAttribute>();
          if (attr != null)
            result[i] = attr.GetShortName();
          else
            result[i] = field.Name;
        }
        enumShortNamesCache.Add(enumType, result);
      }
      return result;
    }

    private static string FormatEnumValue(Type enumType, object enumValue)
    {
      if(!enumCacheSorted.TryGetValue(enumType, out var sorted))
      {
        var names = GetEnumDisplayShortNames(enumType);
        var values = GetEnumValues(enumType);
        sorted = values.Zip(names, (val, name) => (val, name)).OrderBy(tuple => tuple.val).ToArray();
        enumCacheSorted.Add(enumType, sorted);
      }

      var value = Convert.ToUInt64(enumValue);
      bool isFlags = enumType.GetCustomAttribute<FlagsAttribute>() != null;
      // Fast-path - zero value.
      if (value == 0L)
      {
        if (sorted.Length != 0 && sorted[0].Value == 0L)
          return sorted[0].Name;
        return "<None>";
      }
      var result = new StringBuilder();
      ulong saveValue = value;
      // Make the string in reverse order - if there's compound values, they'll be first and we'll exclude their components from the value,
      // thus minimizing the resulting string.
      for(int i = sorted.Length - 1; i >= 0; i--)
      {
        if ((value & sorted[i].Value) == sorted[i].Value)
        {
          value -= sorted[i].Value;
          if (result.Length > 0)
            result.Insert(0, ", ");
          if (!isFlags)
            return sorted[i].Name; // bypass string building if it's not flags enum, as there will be only one value.
          result.Insert(0, sorted[i].Name);
        }
      }
      // There's some value that's not defined in the enum? Not sure how to deal with that, so let the enum deal with it instead.
      if (value != 0L)
        return Enum.Format(enumType, enumValue, "G");

      return result.ToString();
    }
  }
}
