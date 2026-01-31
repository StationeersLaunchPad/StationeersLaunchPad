
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.UI;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace StationeersLaunchPad
{
  public abstract class Platform
  {
    public static readonly Platform Current = CheckIsServer()
      ? new ServerPlatform()
      : new ClientPlatform();

    private static bool CheckIsServer() =>
          Application.isBatchMode ||
          Application.platform
            is RuntimePlatform.WindowsServer
            or RuntimePlatform.LinuxServer;

    public static bool IsServer => Current.PlatformIsServer;
    protected abstract bool PlatformIsServer { get; }

    public static ConfigDefaults ConfigDefaults => Current.PlatformConfigDefaults;
    protected abstract ConfigDefaults PlatformConfigDefaults { get; }

    public static bool LinuxPathSeparator => Path.DirectorySeparatorChar != '\\';

    public static LoadState InitLoadState => Current.PlatformInitLoadState;
    protected abstract LoadState PlatformInitLoadState { get; }

    public static UniTask Wait(StageWait wait) => Current.PlatformWait(wait);
    protected abstract UniTask PlatformWait(StageWait wait);

    public static UniTask<bool> ContinueAfterUpdate() =>
      Current.PlatformContinueAfterUpdate();
    protected abstract UniTask<bool> PlatformContinueAfterUpdate();

    public static bool PauseOnDepNotice => Current.PlatformPauseOnDepNotice;
    protected abstract bool PlatformPauseOnDepNotice { get; }

    public static void SetBackgroundEnabled(bool enabled) =>
      Current.PlatformSetBg(enabled);
    protected abstract void PlatformSetBg(bool enabled);
  }

  public class ClientPlatform : Platform
  {
    protected override bool PlatformIsServer => false;

    protected override ConfigDefaults PlatformConfigDefaults => new()
    {
      CheckForUpdate = true,
      AutoUpdateOnStart = true,
      LinuxPathPatch = LinuxPathSeparator,
    };

    protected override LoadState PlatformInitLoadState => new()
    {
      AutoLoad = Configs.AutoLoadOnStart.Value,
      SteamDisabled = Configs.DisableSteamOnStart.Value,
    };

    protected override async UniTask PlatformWait(StageWait wait)
    {
      while (!wait.Done) await UniTask.Yield();
    }

    protected override UniTask<bool> PlatformContinueAfterUpdate() =>
      AlertPopup.PostUpdateRestartDialog();

    protected override bool PlatformPauseOnDepNotice => true;

    private bool bgEnabled = true;
    protected override void PlatformSetBg(bool enabled)
    {
      if (enabled == bgEnabled)
        return;
      bgEnabled = enabled;

      var canvas = SceneManager.GetActiveScene()
        .GetRootGameObjects()
        .FirstOrDefault(go => go.name == "Canvas");
      if (canvas == null)
        return;

      foreach (var img in canvas.GetComponentsInChildren<Image>())
        img.color = enabled ? Color.white : Color.black;
    }
  }

  public class ServerPlatform : Platform
  {
    protected override bool PlatformIsServer => true;

    protected override ConfigDefaults PlatformConfigDefaults => new()
    {
      CheckForUpdate = false,
      AutoUpdateOnStart = false,
      LinuxPathPatch = LinuxPathSeparator,
    };

    protected override LoadState PlatformInitLoadState => new()
    {
      AutoLoad = true,
      SteamDisabled = true,
    };

    protected override async UniTask PlatformWait(StageWait wait)
    {
      // don't wait on server
      if (wait.Auto)
        return;

      Logger.Global.LogError("An error occurred during loading. Exiting");
      Application.Quit();
    }

    protected async override UniTask<bool> PlatformContinueAfterUpdate()
    {
      Logger.Global.LogWarning("LaunchPad has updated. Exiting");
      Application.Quit();
      return false;
    }

    protected override bool PlatformPauseOnDepNotice => false;

    protected override void PlatformSetBg(bool enabled) { }
  }
}