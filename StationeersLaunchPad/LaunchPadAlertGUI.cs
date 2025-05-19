using Assets.Scripts;
using ImGuiNET;
using System;
using System.Collections.Generic;
using UI.ImGuiUi;
using UnityEngine;

namespace StationeersLaunchPad {
  public static class LaunchPadAlertGUI {
    public static bool IsActive;
    public static string Title;
    public static string Description;

    public static Dictionary<string, Func<bool>> Buttons = new();

    public static void DrawPreload() {
      if (GameManager.IsBatchMode)
        return;

      DrawAlert();
    }

    public static void Show(string title, string description, Dictionary<string, Func<bool>> buttons) {
      IsActive = true;
      Title = title;
      Description = description;

      if (buttons != null)
        Buttons = buttons;
    }

    public static void Close() {
      IsActive = false;
      Title = string.Empty;
      Description = string.Empty;

      Buttons.Clear();
    }

    internal static void DrawAlert() {
      var size = new Vector2(600, 200);
      var screenSize = ImguiHelper.ScreenSize;
      var center = (screenSize / 2) - (size / 2);
      var buttonSize = new Vector2(size.x / Buttons.Count, 35);
      var buttonPadding = new Vector2(5, 0);

      LaunchPadGUI.PushDefaultStyle();

      ImGui.SetNextWindowSize(size);
      ImGui.SetNextWindowPos(center);
      ImGui.SetNextWindowFocus();
      ImGui.Begin($"{Title}##popup", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);

      ImGui.TextWrapped(Description);

      ImGui.SetCursorPosY(size.y - (buttonSize.y + 10));
      ImGui.Separator();

      ImGui.SetCursorPosX(5);
      foreach (var button in Buttons) {
        var text = button.Key;
        var clicked = button.Value;

        if (ImGui.Button(text, buttonSize - buttonPadding)) {
          if (clicked?.Invoke() == true) {
            Close();
          }
        }
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() - 5);

        ImGui.SameLine();
      }

      ImGui.End();

      LaunchPadGUI.PopDefaultStyle();
    }
  }
}
