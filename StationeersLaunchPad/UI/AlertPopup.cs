using Cysharp.Threading.Tasks;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using UI.ImGuiUi;
using UnityEngine;

namespace StationeersLaunchPad.UI
{
  public static class AlertPopup
  {
    public static Vector2 DefaultSize => new Vector2(600, 200);
    public static Vector2 DefaultPosition => ImguiHelper.ScreenSize / 2;

    private static bool IsActive = false;

    private static string Title;
    private static string Description;

    private static Vector2 Size;
    private static Vector2 CenterPosition;

    private static List<(string, Func<bool>)> Buttons;

    public static void Draw()
    {
      if (!IsActive)
        return;

      ImGuiHelper.Draw(() => DrawAlert());
    }

    public static async UniTask Show(string title, string description, Vector2 size, Vector2 position, params (string, Func<bool>)[] buttons)
    {
      IsActive = buttons != null;
      Title = title;
      Description = description;
      Size = size;
      CenterPosition = position;

      Buttons = buttons?.ToList();

      await WaitUntilClose();
    }

    public static async UniTask Show(string title, string description, Vector2 size, Vector2 position, List<(string, Func<bool>)> buttons)
    {
      IsActive = buttons != null;
      Title = title;
      Description = description;
      Size = size;
      CenterPosition = position;

      Buttons = buttons?.ToList();

      await WaitUntilClose();
    }

    private static async UniTask WaitUntilClose()
    {
      while (IsActive)
        await UniTask.Yield();
    }

    public static void Close()
    {
      IsActive = false;
      Title = string.Empty;
      Description = string.Empty;
      Size = DefaultSize;
      CenterPosition = DefaultPosition;

      Buttons = null;
    }

    private static void DrawAlert()
    {
      ImGui.SetNextWindowSize(Size);
      ImGui.SetNextWindowPos(CenterPosition - Size / 2);
      ImGui.SetNextWindowFocus();
      ImGui.Begin($"{Title}##popup", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);

      ImGuiHelper.TextWrapped(Description);

      var buttonSize = new Vector2((Size.x / Buttons?.Count) ?? 1, 35);

      ImGui.SetCursorPosY(Size.y - (buttonSize.y + 10));
      ImGui.Separator();

      ImGui.SetCursorPosX(5);
      foreach ((var text, var clicked) in Buttons)
      {
        if (ImGui.Button(text, buttonSize - new Vector2(5, 0)))
        {
          if (clicked?.Invoke() == true)
          {
            Close();
          }
        }
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() - 5);

        ImGui.SameLine();
      }

      ImGui.End();
    }

    // returns true if loading should continue
    public static async UniTask<bool> PostUpdateRestartDialog()
    {
      var continueLoad = true;
      bool restartGame()
      {
        ProcessUtil.RestartGame();
        return false;
      }
      bool stopLoad()
      {
        continueLoad = false;
        return true;
      }
      await Show(
        "Restart Recommended",
        "StationeersLaunchPad has been updated, it is recommended to restart the game.",
        DefaultSize,
        DefaultPosition,
        ("Restart Game", restartGame),
        ("Continue Loading", () => true),
        ("Close", stopLoad)
      );
      return continueLoad;
    }

    // returns true if update should happen
    public static async UniTask<bool> ShouldUpdateDialog(
      string tagName, string description, string url)
    {
      var doUpdate = false;
      bool acceptUpdate()
      {
        doUpdate = true;
        return true;
      }
      bool openGithub()
      {
        Application.OpenURL(url);
        return false;
      }
      bool rejectUpdate()
      {
        doUpdate = false;
        return true;
      }

      await Show(
        "Update Available",
        $"StationeersLaunchPad {tagName} is available, would you like to automatically download and update?\n\n{description}",
        new Vector2(800, 400),
        DefaultPosition,
        ("Yes", acceptUpdate),
        ("View release info", openGithub),
        ("No", rejectUpdate)
      );
      return doUpdate;
    }
  }
}
