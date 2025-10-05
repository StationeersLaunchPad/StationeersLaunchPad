using Assets.Scripts;
using Assets.Scripts.Networking;
using Assets.Scripts.Networking.Transports;
using Assets.Scripts.Serialization;
using Assets.Scripts.UI;
using BepInEx;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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
    public const string pluginVersion = "0.2.14";

    private static Harmony harmony;

    private void Awake()
    {
      if (Harmony.HasAnyPatches(pluginGuid))
        return;

      // If the windows steamworks assembly is not found, try to replace it with the linux one
      AppDomain.CurrentDomain.AssemblyResolve += (_, args) => args.Name == "Facepunch.Steamworks.Win64"
          ? AppDomain.CurrentDomain.GetAssemblies().First(assembly => assembly.GetName().Name == "Facepunch.Steamworks.Posix")
          : null;

      // *** DO NOT REFERENCE LaunchPadConfig FIELDS IN THIS METHOD ***
      // referencing LaunchPadConfig fields in this method will force the class to initialize before the method starts
      // we need to add the resolve hook above before LaunchPadConfig initializes so the steamworks types will be valid on linux

      harmony = new Harmony(pluginGuid);
      harmony.PatchAll();

      var unityLogger = Debug.unityLogger as UnityEngine.Logger;
      unityLogger.logHandler = new LogWrapper(unityLogger.logHandler);

      var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
      PlayerLoopHelper.Initialize(ref playerLoop);

      LaunchPadConfig.Run(this.Config);
    }

    public static void RunLinuxPathPatch() => harmony.CreateClassProcessor(typeof(LinuxPathPatch), true).Patch();
  }

  [HarmonyPatch]
  internal static class LaunchPadPatches
  {
    [HarmonyPatch(typeof(SplashBehaviour), "Awake"), HarmonyPrefix]
    private static bool SplashAwake(ref SplashBehaviour __instance)
    {
      LaunchPadConfig.SplashBehaviour = __instance;
      Application.targetFrameRate = 60;
      typeof(SplashBehaviour).GetProperty("IsActive").SetValue(null, true);

      return false;
    }

    [HarmonyPatch(typeof(SplashBehaviour), nameof(SplashBehaviour.Draw)), HarmonyPrefix]
    private static bool SplashDraw()
    {
      if (GameManager.IsBatchMode)
        return true;

      if (LaunchPadLoaderGUI.IsActive)
        LaunchPadLoaderGUI.Draw();

      if (LaunchPadConsoleGUI.IsActive)
        LaunchPadConsoleGUI.Draw();

      if (LaunchPadConfigGUI.IsActive)
        LaunchPadConfigGUI.Draw();

      if (LaunchPadAlertGUI.IsActive)
        LaunchPadAlertGUI.Draw();

      LaunchPadLoaderGUI.IsActive = LaunchPadConfig.LoadState != LoadState.Running;

      return !LaunchPadLoaderGUI.IsActive && !LaunchPadConsoleGUI.IsActive && !LaunchPadConfigGUI.IsActive && !LaunchPadAlertGUI.IsActive;
    }

    [HarmonyPatch(typeof(WorldManager), "LoadDataFiles"), HarmonyPostfix]
    private static void LoadDataFiles() =>
      // Some global menus (printer recipe selection, hash generator selection) load strings
      // before mod xml files are loaded in. This forces a reload of all those strings.
      Localization.OnLanguageChanged.Invoke();

    [HarmonyPatch(typeof(SteamUGC), nameof(SteamUGC.DeleteFileAsync)), HarmonyPrefix]
    private static bool DeleteFileAsync(ref Task<bool> __result)
    {
      // don't remove workshop items when the owner unsubscribes
      __result = Task.Run(() => true);
      return false;
    }

    // we patch PublishMod to add the changelog, but its done in 2 steps.
    // first we prefix patch PublishMod to save the changelog
    // then we prefix patch Workshop_PublishItemAsync to add the saved changelog
    // we check the directory path of the mod matches just to be sure something weird didnt happen
    private static string SavedChangeLog = "";
    private static string SavedPath = "";
    [HarmonyPatch(typeof(WorkshopMenu), "PublishMod"), HarmonyPrefix]
    private static void PublishMod(WorkshopModListItem ____selectedModItem)
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
    private static void Workshop_PublishItemAsync(SteamTransport.WorkShopItemDetail detail)
    {
      if (detail != null && detail.Path == SavedPath)
        detail.ChangeNote = SavedChangeLog;
    }

    [HarmonyPatch(typeof(WorkshopMenu), "SaveWorkShopFileHandle"), HarmonyPrefix]
    private static bool WorkshopMenu_SaveWorkShopFileHandle(SteamTransport.WorkShopItemDetail ItemDetail, ModData mod)
    {
      // If we can deserialize it as our extended mod metadata, use that instead
      var about = XmlSerialization.Deserialize<ModAbout>(mod.AboutXmlPath, "ModMetadata");
      if (about != null)
      {
        about.WorkshopHandle = ItemDetail.PublishedFileId;
        _ = about.SaveXml(mod.AboutXmlPath);
        return false;
      }
      return true;
    }

    [HarmonyPatch(typeof(WorkshopMenu), "SelectMod"), HarmonyPostfix]
    private static void WorkshopMenuSelectMod(WorkshopMenu __instance, WorkshopModListItem modItem)
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
      __instance.DescriptionText.text = !string.IsNullOrEmpty(modIGDescription) ? modIGDescription : TextFormatting.SteamToTMP(modAbout.Description);
    }

    [HarmonyPatch(typeof(WorkshopMenu), nameof(WorkshopMenu.ManagerAwake)), HarmonyPostfix]
    private static void WorkshopMenuAwake(WorkshopMenu __instance)
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
    private static void WorkshopMenuDrawConfig()
    {
      if (LaunchPadConsoleGUI.IsActive)
        LaunchPadConsoleGUI.Draw();

      if (LaunchPadConfigGUI.IsActive)
        LaunchPadConfigGUI.Draw();

      if (LaunchPadAlertGUI.IsActive)
        LaunchPadAlertGUI.Draw();

      if (!WorkshopMenu.Instance.isActiveAndEnabled)
        return;

      workshopMenuSelectedField ??= typeof(WorkshopMenu).GetField("_selectedModItem", BindingFlags.Instance | BindingFlags.NonPublic);

      var selectedModItem = workshopMenuSelectedField.GetValue(WorkshopMenu.Instance) as WorkshopModListItem;
      if (selectedModItem == null)
        return;

      var modData = selectedModItem.Data;
      if (modData == null)
        return;

      LaunchPadConfigGUI.DrawWorkshopConfig(modData);
    }

    [HarmonyPatch(typeof(SteamClient), nameof(SteamClient.Init)), HarmonyPrefix]
    private static bool SteamClient_Init(uint appid, bool asyncCallbacks) =>
      // If its already initialized, just skip instead of erroring
      // We initialize this before the game does, but we still want the game to think it initialized itself
      !SteamClient.IsValid;

    [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.Init), typeof(TransportType)), HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> NetworkManager_Init(IEnumerable<CodeInstruction> instructions)
    {
      var matcher = new CodeMatcher(instructions);
      _ = matcher.MatchStartForward(new CodeInstruction(OpCodes.Newobj, typeof(MetaServerTransport).GetConstructor(Type.EmptyTypes)));
      if (!matcher.IsValid)
      {
        Logger.Global.LogError("Could not patch NetworkManager.Init. Steam integration may not work properly");
        return instructions;
      }
      _ = matcher.RemoveInstruction();
      _ = matcher.Insert(CodeInstruction.Call(() => GetMetaServerTransport()));
      return matcher.Instructions();
    }

    private static MetaServerTransport metaServerTransport;
    public static MetaServerTransport GetMetaServerTransport()
    {
      metaServerTransport ??= new();
      return metaServerTransport;
    }

    [HarmonyPatch(typeof(MetaServerTransport), nameof(MetaServerTransport.InitClient)), HarmonyPrefix]
    private static bool MetaServerTransport_InitClient(MetaServerTransport __instance) =>
      // don't double initialize
      !__instance.IsInitialised;

    [HarmonyPatch(typeof(GameString), "op_Implicit", new Type[] { typeof(GameString) }), HarmonyPrefix]
    private static bool GameString_op_Implicit(ref string __result, GameString gameString)
    {
      __result = gameString?.DisplayString ?? string.Empty;

      return false;
    }

    [HarmonyPatch(typeof(StationSaveUtils), nameof(StationSaveUtils.DefaultPath), MethodType.Getter), HarmonyPrefix]
    private static bool StationSaveUtils_DefaultPath(ref string __result)
    {
      if (string.IsNullOrEmpty(LaunchPadConfig.SavePath))
        return true;

      __result = Path.IsPathRooted(LaunchPadConfig.SavePath)
        ? LaunchPadConfig.SavePath
        : Path.Combine(!GameManager.IsBatchMode ?
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "My Games") :
            StationSaveUtils.ExeDirectory.FullName,
          LaunchPadConfig.SavePath);
      return false;
    }
  }

  // no HarmonyPatch on class so it isn't auto-run
  internal static class LinuxPathPatch
  {
    [HarmonyPatch(typeof(WorldManager), "LoadDataFilesAtPath", typeof(string)), HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> WorldManager_LoadDataFilesAtPath(IEnumerable<CodeInstruction> instructions)
    {
      var matcher = new CodeMatcher(instructions);

      while (true)
      {
        _ = matcher.MatchStartForward(new CodeInstruction(OpCodes.Ldstr));
        if (matcher.IsInvalid)
          break;
        var curStr = matcher.Instruction.operand as string;
        if (curStr.Contains('\\'))
        {
          var replaced = curStr.Replace('\\', Path.DirectorySeparatorChar);
          matcher.Instruction.operand = replaced;
          Logger.Global.LogDebug($"WorldManager.LoadDataFilesAtPath patched: {curStr} => {replaced}");
        }
        _ = matcher.Advance(1);
      }

      return matcher.Instructions();
    }
  }
}