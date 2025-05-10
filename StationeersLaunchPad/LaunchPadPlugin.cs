using System.Reflection;
using System.Threading.Tasks;
using Assets.Scripts;
using Assets.Scripts.Networking.Transports;
using Assets.Scripts.Serialization;
using BepInEx;
using BepInEx.Configuration;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using UnityEngine.LowLevel;

namespace StationeersLaunchPad
{
  [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
  public class LaunchPadPlugin : BaseUnityPlugin
  {
    public const string pluginGuid = "stationeers.launchpad";
    public const string pluginName = "StationeersLaunchPad";
    public const string pluginVersion = "0.1.5";

    void Awake()
    {
      if (Harmony.HasAnyPatches(pluginGuid))
        return;

      LaunchPadConfig.AutoLoadOnStart = this.Config.Bind<bool>(
        new ConfigDefinition("Startup", "AutoLoadOnStart"),
        defaultValue: true,
        configDescription: new ConfigDescription(
          "Automatically load after the configured wait time on startup. Can be stopped by clicking the loading window at the bottom"
        )
       );
       LaunchPadConfig.AutoUpdateOnStart = this.Config.Bind<bool>(
         new ConfigDefinition("Startup", "AutoUpdateOnStart"),
         defaultValue: !GameManager.IsBatchMode, // Default to false on DS
         configDescription: new ConfigDescription(
           "Automatically update mod loader on startup."
         )
       );
      LaunchPadConfig.AutoLoadWaitTime = this.Config.Bind<int>(
        new ConfigDefinition("Startup", "AutoLoadWaitTime"),
        defaultValue: 3,
        configDescription: new ConfigDescription(
          "How many seconds to wait before loading mods, then loading the game",
          new AcceptableValueRange<int>(3, 30)
        )
      );
      LaunchPadConfig.AutoSort = this.Config.Bind<bool>(
        new ConfigDefinition("Startup", "AutoSort"),
        defaultValue: true,
        configDescription: new ConfigDescription(
          "Automatically sort based on LoadBefore/LoadAfter tags in mod data"
        )
      );
      LaunchPadConfig.SortedConfig = new SortedConfigFile(this.Config);

      var harmony = new Harmony(pluginGuid);
      harmony.PatchAll();

      var unityLogger = Debug.unityLogger as UnityEngine.Logger;
      unityLogger.logHandler = new LogWrapper(unityLogger.logHandler);

      var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
      PlayerLoopHelper.Initialize(ref playerLoop);

      LaunchPadConfig.Run();
    }
  }

  [HarmonyPatch]
  static class LaunchPadPatches
  {
    [HarmonyPatch(typeof(SplashBehaviour), "Awake"), HarmonyPrefix]
    static bool SplashAwake(SplashBehaviour __instance)
    {
      LaunchPadConfig.SplashBehaviour = __instance;
      Application.targetFrameRate = 60;
      typeof(SplashBehaviour).GetProperty("IsActive").SetValue(null, true);
      return false;
    }

    [HarmonyPatch(typeof(SplashBehaviour), nameof(SplashBehaviour.Draw)), HarmonyPrefix]
    static bool SplashDraw()
    {
      if (LaunchPadGUI.IsActive)
      {
        LaunchPadGUI.DrawPreload();
        return false;
      }
      return true;
    }

    [HarmonyPatch(typeof(WorldManager), "LoadDataFiles"), HarmonyPostfix]
    static void LoadDataFiles()
    {
      // Some global menus (printer recipe selection, hash generator selection) load strings
      // before mod xml files are loaded in. This forces a reload of all those strings.
      Localization.OnLanguageChanged.Invoke();
    }

    [HarmonyPatch(typeof(SteamUGC), nameof(SteamUGC.DeleteFileAsync)), HarmonyPrefix]
    static bool DeleteFileAsync(ref Task<bool> __result)
    {
      // don't remove workshop items when the owner unsubscribes
      __result = Task.Run(() => true);
      return false;
    }

    // we patch PublishMod to add the changelog, but its done in 2 steps.
    // first we prefix patch PublishMod to save the changelog
    // then we prefix patch Workshop_PublishItemAsync to add the saved changelog
    // we check the directory path of the mod matches just to be sure something weird didnt happen
    static string SavedChangeLog = "";
    static string SavedPath = "";
    [HarmonyPatch(typeof(WorkshopMenu), "PublishMod"), HarmonyPrefix]
    static void PublishMod(WorkshopModListItem ____selectedModItem)
    {
      var mod = ____selectedModItem.Data;
      var about = XmlSerialization.Deserialize<ModAbout>(mod.AboutXmlPath, "ModMetadata");
      SavedChangeLog = about.ChangeLog;
      SavedPath = mod.DirectoryPath;
    }
    [HarmonyPatch(typeof(SteamTransport), nameof(SteamTransport.Workshop_PublishItemAsync)), HarmonyPrefix]
    static void Workshop_PublishItemAsync(SteamTransport.WorkShopItemDetail detail)
    {
      if (detail.Path == SavedPath)
        detail.ChangeNote = SavedChangeLog;
    }

    [HarmonyPatch(typeof(WorkshopMenu), "SelectMod"), HarmonyPostfix]
    static void WorkshopMenuSelectMod(WorkshopMenu __instance, WorkshopModListItem modItem)
    {
      var modInfo = LaunchPadConfig.Mods.Find(mod => mod.Path == modItem?.Data?.DirectoryPath);
      var inGameDesc = modInfo?.About?.InGameDescription?.Value;
      if (!string.IsNullOrEmpty(inGameDesc))
        __instance.DescriptionText.text = inGameDesc;
    }

    private static FieldInfo workshopMenuSelectedField;

    [HarmonyPatch(typeof(OrbitalSimulation), nameof(OrbitalSimulation.Draw)), HarmonyPrefix]
    static void WorkshopMenuDrawConfig()
    {
      if (!WorkshopMenu.Instance.isActiveAndEnabled)
        return;

      if (workshopMenuSelectedField == null)
        workshopMenuSelectedField = typeof(WorkshopMenu).GetField("_selectedModItem", BindingFlags.Instance | BindingFlags.NonPublic);

      var modData = ((WorkshopModListItem)workshopMenuSelectedField.GetValue(WorkshopMenu.Instance)).Data;
      LaunchPadGUI.DrawMenuConfig(modData);
    }

    [HarmonyPatch(typeof(SteamClient), nameof(SteamClient.Init)), HarmonyPrefix]
    static bool SteamClient_Init(uint appid, bool asyncCallbacks)
    {
      // If its already initialized, just skip instead of erroring
      // We initialize this before the game does, but we still want the game to think it initialized itself
      return !SteamClient.IsValid;
    }
  }
}