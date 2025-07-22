using BepInEx.Configuration;
using ImGuiNET;
using System;
using System.Reflection;
using UI.ImGuiUi;
using UnityEngine;

namespace StationeersLaunchPad
{
  internal class LaunchPadConfigGUI
  {
    public static bool IsActive = false;

    public static void Draw()
    {
      ImGuiHelper.Draw(() => DrawConfigEditor());
    }

    public static void DrawWorkshopConfig(ModData modData)
    {
      if (modData == null)
        LaunchPadLoaderGUI.SelectedInfo = null;
      else if (LaunchPadLoaderGUI.SelectedMod == null || LaunchPadLoaderGUI.SelectedInfo.Path != modData.DirectoryPath)
        LaunchPadLoaderGUI.SelectedInfo = LaunchPadConfig.Mods.Find(mod => mod.Path == modData.DirectoryPath);

      var screenSize = ImguiHelper.ScreenSize;
      var padding = new Vector2(25, 25);
      var topLeft = new Vector2(screenSize.x - 800f - padding.x, padding.y);
      var bottomRight = screenSize - padding;

      ImGuiHelper.Draw(() =>
      {
        ImGui.SetNextWindowSize(bottomRight - topLeft);
        ImGui.SetNextWindowPos(topLeft);
        if (ImGui.Begin("Mod Configuration##menuconfig", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings))
          DrawConfigEditor();
        ImGui.End();
      });
    }

    public static void DrawConfigEditor()
    {
      if (LaunchPadLoaderGUI.SelectedInfo == null || LaunchPadLoaderGUI.SelectedInfo.Source == ModSource.Core)
      {
        ImGuiHelper.TextDisabled("Select a mod to edit configuration");
        return;
      }

      if (LaunchPadLoaderGUI.SelectedMod == null)
      {
        ImGuiHelper.TextDisabled("Mod was not enabled at load time or is not configurable.");
        return;
      }

      var configFiles = LaunchPadLoaderGUI.SelectedMod.GetSortedConfigs();
      if (configFiles == null || configFiles.Count == 0)
      {
        ImGuiHelper.TextDisabled($"{LaunchPadLoaderGUI.SelectedInfo.DisplayName} does not have any configuration");
        return;
      }

      ImGuiHelper.TextDisabled("These configurations may require a restart to apply");
      ImGui.BeginChild("##config", ImGuiWindowFlags.HorizontalScrollbar);
      foreach (var configFile in configFiles)
      {
        DrawConfigFile(configFile);
      }
      ImGui.EndChild();
    }

    public static void DrawConfigFile(SortedConfigFile configFile, Func<string, bool> except = null)
    {
      ImGuiHelper.Text(configFile.FileName);
      ImGui.PushID(configFile.FileName);

      foreach (var category in configFile.Categories)
      {
        if (except?.Invoke(category.Category) ?? false)
          continue;

        if (!ImGui.CollapsingHeader(category.Category, ImGuiTreeNodeFlags.DefaultOpen))
          continue;

        foreach (var entry in category.Entries)
        {
          DrawConfigEntry(entry);
        }
      }

      ImGui.PopID();
    }

    public static bool DrawConfigEntry(ConfigEntryBase entry, bool fill = true)
    {
      var changed = false;
      ImGui.PushID(entry.Definition.Key);
      ImGui.BeginGroup();

      ImGuiHelper.Text(entry.Definition.Key);
      ImGui.SameLine();
      if (fill)
        ImGui.SetNextItemWidth(-float.Epsilon);

      var value = entry.BoxedValue;
      changed = value switch
      {
        Color => DrawColorEntry(entry as ConfigEntry<Color>),
        Vector2 => DrawVector2Entry(entry as ConfigEntry<Vector2>),
        Vector3 => DrawVector3Entry(entry as ConfigEntry<Vector3>),
        Vector4 => DrawVector4Entry(entry as ConfigEntry<Vector4>),

        Enum => DrawEnumEntry(entry, value as Enum),
        string => DrawStringEntry(entry as ConfigEntry<string>),
        char => DrawCharEntry(entry as ConfigEntry<char>),
        bool => DrawBoolEntry(entry as ConfigEntry<bool>),
        float => DrawFloatEntry(entry as ConfigEntry<float>),
        double => DrawDoubleEntry(entry as ConfigEntry<double>),
        decimal => DrawDecimalEntry(entry as ConfigEntry<decimal>),
        byte => DrawByteEntry(entry as ConfigEntry<byte>),
        sbyte => DrawSByteEntry(entry as ConfigEntry<sbyte>),
        short => DrawShortEntry(entry as ConfigEntry<short>),
        ushort => DrawUShortEntry(entry as ConfigEntry<ushort>),
        int => DrawIntEntry(entry as ConfigEntry<int>),
        uint => DrawUIntEntry(entry as ConfigEntry<uint>),
        long => DrawLongEntry(entry as ConfigEntry<long>),
        ulong => DrawULongEntry(entry as ConfigEntry<ulong>),
        _ => DrawDefault(entry),
      };

      ImGui.EndGroup();
      ImGui.PopID();

      var description = entry.Description?.Description;
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

    private static bool DrawVector2Entry(ConfigEntry<Vector2> entry)
    {
      var changed = false;

      var value = entry.Value;
      var x = value.x;
      ImGui.Spacing();
      ImGuiHelper.Text("X:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector2valuex", ref x))
      {
        entry.BoxedValue = new Vector2(x, value.y);
        changed = true;
      }

      var y = value.y;
      ImGuiHelper.Text("Y:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector2valuey", ref y))
      {
        entry.BoxedValue = new Vector2(value.x, y);
        changed = true;
      }

      return changed;
    }

    private static bool DrawVector3Entry(ConfigEntry<Vector3> entry)
    {
      var changed = false;

      var value = entry.Value;
      var x = value.x;
      ImGui.Spacing();
      ImGuiHelper.Text("X:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector3valuex", ref x))
      {
        entry.BoxedValue = new Vector3(x, value.y, value.z);
        changed = true;
      }

      var y = value.y;
      ImGuiHelper.Text("Y:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector3valuey", ref y))
      {
        entry.BoxedValue = new Vector3(value.x, y, value.z);
        changed = true;
      }

      var z = value.z;
      ImGuiHelper.Text("Z:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector3valuez", ref z))
      {
        entry.BoxedValue = new Vector3(value.x, value.y, z);
        changed = true;
      }

      return changed;
    }

    private static bool DrawVector4Entry(ConfigEntry<Vector4> entry)
    {
      var changed = false;

      var value = entry.Value;
      var x = value.x;
      ImGui.Spacing();
      ImGuiHelper.Text("X:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector4valuex", ref x))
      {
        entry.BoxedValue = new Vector4(x, value.y, value.z, value.w);
        changed = true;
      }

      var y = value.y;
      ImGuiHelper.Text("Y:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector4valuey", ref y))
      {
        entry.BoxedValue = new Vector4(value.x, y, value.z, value.w);
        changed = true;
      }

      var z = value.z;
      ImGuiHelper.Text("Z:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector4valuez", ref z))
      {
        entry.BoxedValue = new Vector4(value.x, value.y, z, value.w);
        changed = true;
      }

      var w = value.z;
      ImGuiHelper.Text("W:");
      ImGui.SameLine();
      if (ImGui.InputFloat("##vector4valuew", ref w))
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

    public static bool DrawFloatEntry(ConfigEntry<float> entry)
    {
      var changed = false;

      var value = entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<float> valueRange)
      {
        if (ImGui.SliderFloat("##floatvalue", ref value, valueRange.MinValue, valueRange.MaxValue))
        {
          entry.BoxedValue = value;
          changed = true;
        }
      }
      else if (ImGui.InputFloat("##floatvalue", ref value, step: 0))
      {
        entry.BoxedValue = value;
        changed = true;
      }

      return changed;
    }

    public static unsafe bool DrawDoubleEntry(ConfigEntry<double> entry)
    {
      var changed = false;

      var value = entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<double> valueRange)
      {
        var min = valueRange.MinValue;
        var max = valueRange.MaxValue;
        if (ImGui.SliderScalar("##doublevalue", ImGuiDataType.Double, (IntPtr) (&value), (IntPtr) (&min), (IntPtr) (&max)))
        {
          entry.BoxedValue = value;
          changed = true;
        }
      }
      else if (ImGui.InputDouble("##doublevalue", ref value, step: 0))
      {
        entry.BoxedValue = value;
        changed = true;
      }

      return changed;
    }

    public static unsafe bool DrawDecimalEntry(ConfigEntry<decimal> entry)
    {
      var changed = false;

      var value = (double) entry.Value;
      if (entry.Description.AcceptableValues is AcceptableValueRange<decimal> valueRange)
      {
        var min = valueRange.MinValue;
        var max = valueRange.MaxValue;
        if (ImGui.SliderScalar("##decimalvalue", ImGuiDataType.Double, (IntPtr) (&value), (IntPtr) (&min), (IntPtr) (&max)))
        {
          entry.BoxedValue = value;
          changed = true;
        }
      }
      else if (ImGui.InputDouble("##decimalvalue", ref value, step: 0))
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
