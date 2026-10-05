using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public static class AlertPopup
{
  public static void Close() => SlpDialog.CloseAll();

  // returns true if loading should continue
  public static async UniTask<bool> PostUpdateRestartDialog()
  {
    var choice = await SlpDialog.Show(
      "Restart Recommended",
      "StationeersLaunchPad has been updated, it is recommended to restart the game.",
      DialogTone.Normal,
      new DialogButton("Restart Game", () =>
      {
        ProcessUtil.RestartGame();
        return false;
      }, primary: true),
      new DialogButton("Continue Loading"),
      new DialogButton("Close", cancel: true));
    return choice != 2;
  }

  // returns true if update should happen
  public static async UniTask<bool> ShouldUpdateDialog(string tagName, string description, string url)
  {
    var choice = await SlpDialog.Show(
      "Update Available",
      $"StationeersLaunchPad {tagName} is available, would you like to automatically download and update?\n\n{description}",
      DialogTone.Normal,
      new DialogButton("Yes", primary: true),
      new DialogButton("View release info", () =>
      {
        Application.OpenURL(url);
        return false;
      }),
      new DialogButton("No", cancel: true));
    return choice == 0;
  }
}
