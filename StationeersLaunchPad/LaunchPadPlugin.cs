using BepInEx;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.LowLevel;

namespace StationeersLaunchPad
{
  [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
  public class LaunchPadPlugin : BaseUnityPlugin
  {
    public const string pluginGuid = "stationeers.launchpad";
    public const string pluginName = "StationeersLaunchPad";
    public const string pluginVersion = "0.2.17";

    void Awake()
    {
      if (Harmony.HasAnyPatches(pluginGuid))
        return;

      // If the windows steamworks assembly is not found, try to replace it with the linux one
      AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
      {
        if (args.Name == "Facepunch.Steamworks.Win64")
          return AppDomain.CurrentDomain.GetAssemblies().First(assembly => assembly.GetName().Name == "Facepunch.Steamworks.Posix");
        return null;
      };

      // *** DO NOT REFERENCE LaunchPadConfig FIELDS IN THIS METHOD ***
      // referencing LaunchPadConfig fields in this method will force the class to initialize before the method starts
      // we need to add the resolve hook above before LaunchPadConfig initializes so the steamworks types will be valid on linux

      var patchSuccess = LaunchPadPatches.RunPatches(new Harmony(pluginGuid));

      var unityLogger = Debug.unityLogger as UnityEngine.Logger;
      unityLogger.logHandler = new LogWrapper(unityLogger.logHandler);

      var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
      PlayerLoopHelper.Initialize(ref playerLoop);

      LaunchPadConfig.Run(this.Config, !patchSuccess);
    }
  }
}