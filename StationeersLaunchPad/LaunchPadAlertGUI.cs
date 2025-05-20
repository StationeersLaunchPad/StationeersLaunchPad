using Assets.Scripts;
using Cysharp.Threading.Tasks;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using UI.ImGuiUi;
using UnityEngine;

namespace StationeersLaunchPad {
  public static class LaunchPadAlertGUI {
    public static bool IsActive;
    public static string Title;
    public static string Description;
    public static Vector2 Size = new Vector2(600, 200);
    public static Vector2 DefaultSize = new Vector2(600, 200);

    public static List<(string, Func<bool>)> Buttons;

    public static void DrawPreload() {
      if (GameManager.IsBatchMode)
        return;

      DrawAlert();
    }

    public static async UniTask Show(string title, string description, Vector2 size, params (string, Func<bool>)[] buttons) {
      IsActive = buttons != null;
      Title = title;
      Description = description;
      Size = size;

      Buttons = buttons?.ToList();

      await WaitUntilClose();
    }

    public static async UniTask Show(string title, string description, Vector2 size, List<(string, Func<bool>)> buttons) {
      IsActive = buttons != null;
      Title = title;
      Description = description;
      Size = size;

      Buttons = buttons?.ToList();

      await WaitUntilClose();
    }

    public static async UniTask WaitUntilClose() {
      await UniTask.WaitUntil(() => !IsActive);
    }

    public static void Close() {
      IsActive = false;
      Title = string.Empty;
      Description = string.Empty;
      Size = DefaultSize;

      Buttons?.Clear();
    }

    internal static void DrawAlert() {
      var screenSize = ImguiHelper.ScreenSize;
      var center = (screenSize / 2) - (Size / 2);
      var buttonSize = new Vector2(Size.x / Buttons.Count, 35);
      var buttonPadding = new Vector2(5, 0);

      LaunchPadGUI.PushDefaultStyle();

      ImGui.SetNextWindowSize(Size);
      ImGui.SetNextWindowPos(center);
      ImGui.SetNextWindowFocus();
      ImGui.Begin($"{Title}##popup", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);

      ImGui.TextWrapped(Description);

      ImGui.SetCursorPosY(Size.y - (buttonSize.y + 10));
      ImGui.Separator();

      ImGui.SetCursorPosX(5);
      foreach (var button in Buttons) {
        var text = button.Item1;
        var clicked = button.Item2;

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
