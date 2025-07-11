using Assets.Scripts;
using Assets.Scripts.Networking.Transports;
using Assets.Scripts.Serialization;
using Assets.Scripts.UI;
using BepInEx;
using BepInEx.Configuration;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using Steamworks;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.LowLevel;
using GameString = Assets.Scripts.Localization2.GameString;

namespace StationeersLaunchPad
{
  [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
  public class LaunchPadPlugin : BaseUnityPlugin
  {
    public const string pluginGuid = "stationeers.launchpad";
    public const string pluginName = "StationeersLaunchPad";
    public const string pluginVersion = "0.2.0";

    void Awake()
    {
      if (Harmony.HasAnyPatches(pluginGuid))
        return;
      LaunchPadConfig.DebugMode = this.Config.Bind(
       new ConfigDefinition("Startup", "DebugMode"),
       false,
       new ConfigDescription(
         "If you run into issues with loading mods, or anything else, please enable this for more verbosity in error reports"
       )
      );
      LaunchPadConfig.AutoLoadOnStart = this.Config.Bind(
        new ConfigDefinition("Startup", "AutoLoadOnStart"),
        true,
        new ConfigDescription(
          "Automatically load after the configured wait time on startup. Can be stopped by clicking the loading window at the bottom"
        )
       );
      LaunchPadConfig.CheckForUpdate = this.Config.Bind(
        new ConfigDefinition("Startup", "CheckForUpdate"),
        !GameManager.IsBatchMode, // Default to false on DS
        new ConfigDescription(
          "Automatically check for mod loader updates on startup."
        )
      );
      LaunchPadConfig.AutoUpdateOnStart = this.Config.Bind(
        new ConfigDefinition("Startup", "AutoUpdateOnStart"),
        !GameManager.IsBatchMode, // Default to false on DS
        new ConfigDescription(
          "Automatically update mod loader on startup. Ignored if CheckForUpdate is not also enabled."
        )
      );
      LaunchPadConfig.AutoLoadWaitTime = this.Config.Bind(
        new ConfigDefinition("Startup", "AutoLoadWaitTime"),
        3,
        new ConfigDescription(
          "How many seconds to wait before loading mods, then loading the game",
          new AcceptableValueRange<int>(3, 30)
        )
      );
      LaunchPadConfig.AutoSortOnStart = this.Config.Bind(
        new ConfigDefinition("Startup", "AutoSort"),
        true,
        new ConfigDescription(
          "Automatically sort based on LoadBefore/LoadAfter tags in mod data"
        )
      );
      LaunchPadConfig.DisableSteam = this.Config.Bind(
        new ConfigDefinition("Startup", "DisableSteam"),
        false,
         new ConfigDescription(
          "Don't attempt to load steam workshop mods"
        )
      );
      LaunchPadConfig.StrategyType = this.Config.Bind(
        new ConfigDefinition("Mod Loading", "LoadStrategyType"),
        LoadStrategyType.Linear,
        new ConfigDescription(
          "Linear type loads mods one by one in sequential order. More types of mod loading will be added later."
        )
      );
      LaunchPadConfig.StrategyMode = this.Config.Bind(
        new ConfigDefinition("Mod Loading", "LoadStrategyMode"),
        LoadStrategyMode.Serial,
        new ConfigDescription(
          "Parallel mode loads faster for a large number of mods, but may fail in extremely rare cases. Switch to serial mode if running into loading issues."
        )
      );
      LaunchPadConfig.SavePathOverride = this.Config.Bind(
        new ConfigDefinition("Mod Loading", "SavePathOverride"),
        "",
        new ConfigDescription(
          "This setting allows you to override the default path that config and save files are stored. Notice, due to how this path is implemented in the base game, this setting can only be applied on server start.  Changing it while in game will not have an effect until after a restart."
        )
      );
      LaunchPadConfig.PostUpdateCleanup = this.Config.Bind(
        new ConfigDefinition("Internal", "PostUpdateCleanup"),
        true,
        new ConfigDescription(
          "This setting is automatically managed and should probably not be manually changed. Remove update backup files on start."
        )
      );
      LaunchPadConfig.OneTimeBoosterInstall = this.Config.Bind(
        new ConfigDefinition("Internal", "OneTimeBoosterInstall"),
        true,
        new ConfigDescription(
          "This setting is automatically managed and should probably not be manually changed. Perform one-time download of LaunchPadBooster to update from version that didn't include it."
        )
      );

      var sortedConfig = new SortedConfigFile(this.Config);
      sortedConfig.Categories.Remove(sortedConfig.Categories.Find(cat => cat.Category == "Internal"));
      LaunchPadConfig.SortedConfig = sortedConfig;

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
        LaunchPadGUI.DrawPreload();

      if (LaunchPadAlertGUI.IsActive)
        LaunchPadAlertGUI.DrawAlert();

      return !LaunchPadGUI.IsActive && !LaunchPadAlertGUI.IsActive;
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
      var mod = ____selectedModItem?.Data;
      if (mod == null)
        return;

      var about = XmlSerialization.Deserialize<ModAbout>(mod.AboutXmlPath, "ModMetadata");
      if (about == null)
        return;

      SavedChangeLog = about.ChangeLog;
      SavedPath = mod.DirectoryPath;
    }

    [HarmonyPatch(typeof(SteamTransport), nameof(SteamTransport.Workshop_PublishItemAsync)), HarmonyPrefix]
    static void Workshop_PublishItemAsync(SteamTransport.WorkShopItemDetail detail)
    {
      if (detail != null && detail.Path == SavedPath)
        detail.ChangeNote = SavedChangeLog;
    }

    [HarmonyPatch(typeof(WorkshopMenu), "SelectMod"), HarmonyPostfix]
    static void WorkshopMenuSelectMod(WorkshopMenu __instance, WorkshopModListItem modItem)
    {
      if (modItem == null)
        return;

      var modData = modItem.Data;
      if (modData == null)
        return;

      var modInfo = LaunchPadConfig.Mods.Find((mod) => mod.Path == modData.DirectoryPath);
      if (modInfo == null)
        return;

      var modValid = modInfo.IsWorkshopValid();
      if (!modValid.Item1 && modValid.Item2 != string.Empty)
      {
        AlertPanel.Instance.ShowAlert(modValid.Item2, AlertState.Alert);
        __instance.SelectedModButtonRight?.SetActive(false);
        return;
      }

      var modAbout = modInfo.About;
      if (modAbout == null)
        return;

      var publishButton = __instance.SelectedModButtonRight?.GetComponentInChildren<TextMeshProUGUI>();
      if (modInfo.Source == ModSource.Local && modAbout.WorkshopHandle == 0)
        publishButton.SetText("Publish");
      else if (modInfo.Source == ModSource.Workshop)
        publishButton.SetText("Unsubscribe");
      else
        publishButton.SetText("Update");

      var modIGDescription = modAbout.InGameDescription?.Value;
      if (!string.IsNullOrEmpty(modIGDescription))
        __instance.DescriptionText.text = modIGDescription;
      else
        __instance.DescriptionText.text = TextFormatting.SteamToTMP(modAbout.Description);
    }

    [HarmonyPatch(typeof(WorkshopMenu), nameof(WorkshopMenu.ManagerAwake)), HarmonyPostfix]
    static void WorkshopMenuAwake(WorkshopMenu __instance)
    {
      var handler = __instance.DescriptionText.gameObject.AddComponent<WorkshopMenuLinkHandler>();
      handler.DescriptionText = __instance.DescriptionText;
    }
    private class WorkshopMenuLinkHandler : MonoBehaviour, IPointerClickHandler
    {
      public TextMeshProUGUI DescriptionText;
      public void OnPointerClick(PointerEventData eventData)
      {
        var linkIndex = TMP_TextUtilities.FindIntersectingLink(this.DescriptionText, eventData.position, null);
        if (linkIndex != -1)
        {
          var linkInfo = this.DescriptionText.textInfo.linkInfo[linkIndex];
          Application.OpenURL(linkInfo.GetLinkID());
        }
      }
    }

    private static FieldInfo workshopMenuSelectedField;
    [HarmonyPatch(typeof(OrbitalSimulation), nameof(OrbitalSimulation.Draw)), HarmonyPrefix]
    static void WorkshopMenuDrawConfig()
    {
      if (!WorkshopMenu.Instance.isActiveAndEnabled)
        return;

      if (workshopMenuSelectedField == null)
        workshopMenuSelectedField = typeof(WorkshopMenu).GetField("_selectedModItem", BindingFlags.Instance | BindingFlags.NonPublic);

      var selectedModItem = workshopMenuSelectedField.GetValue(WorkshopMenu.Instance) as WorkshopModListItem;
      if (selectedModItem == null)
        return;

      var modData = selectedModItem.Data;
      if (modData == null)
        return;

      LaunchPadGUI.DrawMenuConfig(modData);
    }

    [HarmonyPatch(typeof(SteamClient), nameof(SteamClient.Init)), HarmonyPrefix]
    static bool SteamClient_Init(uint appid, bool asyncCallbacks)
    {
      // If its already initialized, just skip instead of erroring
      // We initialize this before the game does, but we still want the game to think it initialized itself
      return !SteamClient.IsValid;
    }

    [HarmonyPatch(typeof(GameString), "op_Implicit", new Type[] { typeof(GameString) }), HarmonyPrefix]
    static bool GameString_op_Implicit(ref string __result, GameString gameString)
    {
      __result = gameString?.DisplayString ?? string.Empty;

      return false;
    }

    [HarmonyPatch(typeof(StationSaveUtils), nameof(StationSaveUtils.DefaultPath), MethodType.Getter), HarmonyPrefix]
    static bool StationSaveUtils_DefaultPath(ref string __result)
    {      
      if (string.IsNullOrEmpty(LaunchPadConfig.SavePath))
      {
        return true;
      }

      __result = Path.IsPathRooted(LaunchPadConfig.SavePath)
        ? LaunchPadConfig.SavePath
        : Path.Combine(!GameManager.IsBatchMode ? 
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "My Games") : 
            StationSaveUtils.ExeDirectory.FullName,
          LaunchPadConfig.SavePath);
      return false;
  }
    
  }
}