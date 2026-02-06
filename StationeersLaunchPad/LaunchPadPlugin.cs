using BepInEx;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using StationeersLaunchPad.Commands;
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.LowLevel;
using Util.Commands;

namespace StationeersLaunchPad
{
  [BepInPlugin(LaunchPadInfo.GUID, LaunchPadInfo.NAME, LaunchPadInfo.VERSION)]
  public class LaunchPadPlugin : BaseUnityPlugin
  {
    void Awake()
    {
      if (Harmony.HasAnyPatches(LaunchPadInfo.GUID))
        return;

      // Do not add any more to this method, add to FinishAwake instead.
      // The Steamworks dll redirect needs to be installed before any types
      // that rely on it are initialized. Any types referenced in a method
      // are initialized before the method starts, so this method needs to
      // avoid causing any type initialization before the above method runs
      InstallSteamworksRedirect();
      FinishAwake();
    }

    private static void InstallSteamworksRedirect()
    {
      // If the windows steamworks assembly is not found, try to replace it with the linux one
      AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
      {
        if (args.Name == "Facepunch.Steamworks.Win64")
          return AppDomain.CurrentDomain.GetAssemblies().First(assembly => assembly.GetName().Name == "Facepunch.Steamworks.Posix");
        return null;
      };
    }

    private void FinishAwake()
    {
      var unityLogger = Debug.unityLogger as UnityEngine.Logger;
      unityLogger.logHandler = new LogWrapper(unityLogger.logHandler);

      var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
      PlayerLoopHelper.Initialize(ref playerLoop);

      if (!LaunchPadPatches.RunPatches(new Harmony(LaunchPadInfo.GUID)))
        LaunchPadConfig.StopAutoLoad();

      Configs.Initialize(this.Config);

      if (Configs.LinuxPathPatch.Value)
        LaunchPadPatches.RunLinuxPathPatch();

      try
      {
        RegisterCommand();
      }
      catch (Exception ex)
      {
        StationeersLaunchPad.Logger.Global.LogWarning(
          $"Failed to register SLP command: {ex}");
      }

      LaunchPadConfig.Run();
    }

    private void RegisterCommand()
    {
      CommandLine.AddCommand("slp", SLPCommand.Instance);
    }
  }
}