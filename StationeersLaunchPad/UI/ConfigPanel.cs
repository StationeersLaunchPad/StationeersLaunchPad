using BepInEx.Configuration;
using ImGuiNET;
using StationeersLaunchPad.Loading;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
      ImGuiHelper.Text(wrapper.DisplayName);
      ImGui.SameLine();
      if (fill)
        ImGui.SetNextItemWidth(-float.Epsilon);

      bool changed = false;
      var value = wrapper.BoxedValue;
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
          bool => DrawBoolEntry(wrapper.Entry as ConfigEntry<bool>),
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
        if (requireRestartOriginalValues.TryGetValue(wrapper.Entry, out var originalValue))
        {
          if (Equals(newValue, originalValue))
            requireRestartOriginalValues.Remove(wrapper.Entry);
        }
        else
          requireRestartOriginalValues.Add(wrapper.Entry, value);
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
      var changed = false;

      var value = entry.Value;
      var r = value.r;
      ImGui.Spacing();
      ImGuiHelper.Text($"Red ({r * 255}):");
      if (ImGui.SliderFloat("##colorvaluer", ref r, 0.0f, 1.0f))
      {
        entry.BoxedValue = new Color(r, value.g, value.b, value.a);
        changed = true;
      }

      var g = value.g;
      ImGuiHelper.Text($"Green ({g * 255}):");
      if (ImGui.SliderFloat("##colorvalueg", ref g, 0.0f, 1.0f))
      {
        entry.BoxedValue = new Color(value.r, g, value.b, value.a);
        changed = true;
      }

      var b = value.b;
      ImGuiHelper.Text($"Blue ({b * 255}):");
      if (ImGui.SliderFloat("##colorvalueb", ref b, 0.0f, 1.0f))
      {
        entry.BoxedValue = new Color(value.r, value.g, b, value.a);
        changed = true;
      }

      var a = value.a;
      ImGuiHelper.Text($"Alpha ({a * 255}):");
      if (ImGui.SliderFloat("##colorvaluea", ref a, 0.0f, 1.0f))
      {
        entry.BoxedValue = new Color(value.r, value.g, value.b, a);
        changed = true;
      }

      return changed;
    }

    private static bool DrawVector2Entry(ConfigEntry<Vector2> entry, string format)
    {
      var changed = false;

      var value = entry.Value;
      var x = value.x;
      ImGui.Spacing();
      ImGuiHelper.Text("X:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector2valuex", ref x, format))
      {
        entry.BoxedValue = new Vector2(x, value.y);
        changed = true;
      }

      var y = value.y;
      ImGuiHelper.Text("Y:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector2valuey", ref y, format))
      {
        entry.BoxedValue = new Vector2(value.x, y);
        changed = true;
      }

      return changed;
    }

    private static bool DrawVector3Entry(ConfigEntry<Vector3> entry, string format)
    {
      var changed = false;

      var value = entry.Value;
      var x = value.x;
      ImGui.Spacing();
      ImGuiHelper.Text("X:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector3valuex", ref x, format))
      {
        entry.BoxedValue = new Vector3(x, value.y, value.z);
        changed = true;
      }

      var y = value.y;
      ImGuiHelper.Text("Y:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector3valuey", ref y, format))
      {
        entry.BoxedValue = new Vector3(value.x, y, value.z);
        changed = true;
      }

      var z = value.z;
      ImGuiHelper.Text("Z:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector3valuez", ref z, format))
      {
        entry.BoxedValue = new Vector3(value.x, value.y, z);
        changed = true;
      }

      return changed;
    }

    private static bool DrawVector4Entry(ConfigEntry<Vector4> entry, string format)
    {
      var changed = false;

      var value = entry.Value;
      var x = value.x;
      ImGui.Spacing();
      ImGuiHelper.Text("X:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector4valuex", ref x, format))
      {
        entry.BoxedValue = new Vector4(x, value.y, value.z, value.w);
        changed = true;
      }

      var y = value.y;
      ImGuiHelper.Text("Y:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector4valuey", ref y, format))
      {
        entry.BoxedValue = new Vector4(value.x, y, value.z, value.w);
        changed = true;
      }

      var z = value.z;
      ImGuiHelper.Text("Z:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector4valuez", ref z, format))
      {
        entry.BoxedValue = new Vector4(value.x, value.y, z, value.w);
        changed = true;
      }

      var w = value.z;
      ImGuiHelper.Text("W:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector4valuew", ref w, format))
      {
        entry.BoxedValue = new Vector4(value.x, value.y, value.z, w);
        changed = true;
      }

      return changed;
    }

    public static bool DrawEnumEntry(ConfigEntryBase entry, Enum value)
    {
      var changed = false;
      var currentValue = Convert.ToInt32(value);
      var type = value.GetType();
      var values = Enum.GetValues(type);
      var names = Enum.GetNames(type);
      var index = -1;
      for (var i = 0; i < values.Length; i++)
      {
        if (values.GetValue(i).Equals(value))
        {
          index = i;
          break;
        }
      }

      if (type.GetCustomAttribute<FlagsAttribute>() != null)
      {
        for (var i = 0; i < values.Length; i++)
        {
          for (; i < values.Length; i++)
          {
            var val = (int) values.GetValue(i);
            var newValue = (currentValue & val) == val;
            if (ImGui.Checkbox(names.GetValue(i).ToString(), ref newValue))
            {
              entry.BoxedValue = newValue ? currentValue | val : currentValue & ~val;
              changed = true;
            }
            if (i != values.Length - 1)
              ImGui.SameLine();
          }
        }
      }
      else
      {
        if (ImGui.Combo("##enumvalue", ref index, names, names.Length))
        {
          entry.BoxedValue = values.GetValue(index);
          changed = true;
        }
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

    public static bool DrawBoolEntry(ConfigEntry<bool> entry)
    {
      var changed = false;

      var value = entry.Value;
      if (ImGui.Checkbox("##boolvalue", ref value))
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
  }
}
