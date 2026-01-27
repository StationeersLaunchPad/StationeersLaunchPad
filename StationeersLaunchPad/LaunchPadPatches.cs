
using Assets.Scripts;
using Assets.Scripts.Networking;
using Assets.Scripts.Networking.Transports;
using Assets.Scripts.Serialization;
using Assets.Scripts.UI;
using HarmonyLib;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;
using StationeersLaunchPad.UI;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using GameString = Assets.Scripts.Localization2.GameString;

namespace StationeersLaunchPad
{
  [HarmonyPatch]
  static class LaunchPadPatches
  {
    private static Harmony harmony;

    // Returns true if all patches were successfully applied
    public static bool RunPatches(Harmony harmony)
    {
      LaunchPadPatches.harmony = harmony;

      var failed = false;
      try
      {
        RunEssentialPatches();
      }
      catch
      {
        Logger.Global.LogError("An error occurred running essential patches. LaunchPad will not load");
        throw;
      }

      try
      {
        RunWorkshopPatches();
      }
      catch (Exception ex)
      {
        Logger.Global.LogError("An error occurred running workshop menu patches. The workshop menu may not function properly");
        Logger.Global.LogException(ex);
        failed = true;
      }

      try
      {
        RunSteamPatches();
      }
      catch (Exception ex)
      {
        Logger.Global.LogError("An error occurred running steam patches. Workshop mods may not be loaded properly");
        Logger.Global.LogException(ex);
        failed = true;
      }

      try
      {
        RunBugfixPatches();
      }
      catch (Exception ex)
      {
        Logger.Global.LogError("An error occurred running bugfix patches. Some mods may not work properly");
        Logger.Global.LogException(ex);
        failed = true;
      }

      try
      {
        RunCustomSavePathPatches();
      }
      catch (Exception ex)
      {
        Logger.Global.LogError("An error occurred running custom save path patches. The custom save path feature may not work properly");
        Logger.Global.LogException(ex);
        failed = true;
      }

      return !failed;
    }

    // these are put in separate methods as all class tokens in a method will be resolved before the method starts
    // if one of these patches no longer references a valid class due to a game change, this isolates the error
    private static void RunEssentialPatches() =>
      harmony.CreateClassProcessor(typeof(EssentialPatches), true).Patch();

    private static void RunWorkshopPatches() =>
      harmony.CreateClassProcessor(typeof(WorkshopPatches), true).Patch();

    private static void RunSteamPatches() =>
      harmony.CreateClassProcessor(typeof(SteamPatches), true).Patch();

    private static void RunBugfixPatches() =>
      harmony.CreateClassProcessor(typeof(BugfixPatches), true).Patch();

    private static void RunCustomSavePathPatches() =>
      harmony.CreateClassProcessor(typeof(CustomSavePathPatches), true).Patch();

    public static void RunLinuxPathPatch() =>
      harmony.CreateClassProcessor(typeof(LinuxPathPatch), true).Patch();
  }

  static class EssentialPatches
  {
    [HarmonyPatch(typeof(SplashBehaviour), "Awake"), HarmonyPrefix]
    static bool SplashAwake(SplashBehaviour __instance)
    {
      LaunchPadConfig.SplashBehaviour = __instance;
      Application.targetFrameRate = 60;
      typeof(SplashBehaviour).GetProperty("IsActive").SetValue(null, true);

      return false;
    }

    // set when mod loading is finished
    public static bool GameStarted = false;
    [HarmonyPatch(typeof(SplashBehaviour), nameof(SplashBehaviour.Draw)), HarmonyPrefix]
    static bool SplashDraw()
    {
      if (!Platform.IsServer && !GameStarted)
        LaunchPadConfig.Draw();

      return GameStarted;
    }

    [HarmonyPatch(typeof(WorldManager), "LoadDataFiles"), HarmonyPostfix]
    static void LoadDataFiles()
    {
      // Some global menus (printer recipe selection, hash generator selection) load strings
      // before mod xml files are loaded in. This forces a reload of all those strings.
      Localization.OnLanguageChanged.Invoke();
    }
  }

  static class SteamPatches
  {
    [HarmonyPatch(typeof(SteamClient), nameof(SteamClient.Init)), HarmonyPrefix]
    static bool SteamClient_Init(uint appid, bool asyncCallbacks)
    {
      // If its already initialized, just skip instead of erroring
      // We initialize this before the game does, but we still want the game to think it initialized itself
      return !SteamClient.IsValid;
    }

    [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.Init), typeof(TransportType)), HarmonyTranspiler]
    static IEnumerable<CodeInstruction> NetworkManager_Init(IEnumerable<CodeInstruction> instructions)
    {
      var matcher = new CodeMatcher(instructions);
      matcher.MatchStartForward(new CodeInstruction(OpCodes.Newobj, typeof(MetaServerTransport).GetConstructor(Type.EmptyTypes)));
      if (!matcher.IsValid)
      {
        Logger.Global.LogError("Could not patch NetworkManager.Init. Steam integration may not work properly");
        return instructions;
      }
      matcher.RemoveInstruction();
      matcher.Insert(CodeInstruction.Call(() => GetMetaServerTransport()));
      return matcher.Instructions();
    }

    private static MetaServerTransport metaServerTransport;
    public static MetaServerTransport GetMetaServerTransport()
    {
      if (metaServerTransport == null)
        metaServerTransport = new();
      return metaServerTransport;
    }

    [HarmonyPatch(typeof(MetaServerTransport), nameof(MetaServerTransport.InitClient)), HarmonyPrefix]
    static bool MetaServerTransport_InitClient(MetaServerTransport __instance)
    {
      // don't double initialize
      return !__instance.IsInitialised;
    }
  }

  static class WorkshopPatches
  {
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

      var about = XmlSerialization.Deserialize<ModAboutEx>(mod.AboutXmlPath, "ModMetadata");
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

    [HarmonyPatch(typeof(WorkshopMenu), "SaveWorkShopFileHandle"), HarmonyPrefix]
    static bool WorkshopMenu_SaveWorkShopFileHandle(SteamTransport.WorkShopItemDetail ItemDetail, ModData mod)
    {
      // If we can deserialize it as our extended mod metadata, use that instead
      var about = XmlSerialization.Deserialize<ModAboutEx>(mod.AboutXmlPath, "ModMetadata");
      if (about != null)
      {
        about.WorkshopHandle = ItemDetail.PublishedFileId;
        about.SaveXml(mod.AboutXmlPath);
        return false;
      }
      return true;
    }

    [HarmonyPatch(typeof(WorkshopMenu), "SelectMod"), HarmonyPostfix]
    static void WorkshopMenuSelectMod(WorkshopMenu __instance, WorkshopModListItem modItem)
    {
      if (modItem == null)
        return;

      var modData = modItem.Data;
      if (modData == null)
        return;

      var modInfo = LaunchPadConfig.MatchMod(modData);
      if (modInfo == null)
        return;

      var (valid, error) = Steam.ValidateForWorkshop(modInfo);
      if (!valid && !string.IsNullOrEmpty(error))
      {
        AlertPanel.Instance.ShowAlert(error, AlertState.Alert);
        __instance.SelectedModButtonRight?.SetActive(false);
        return;
      }

      var modAbout = modInfo.About;
      if (modAbout == null)
        return;

      var publishButton = __instance.SelectedModButtonRight?.GetComponentInChildren<TextMeshProUGUI>();
      if (modInfo.Source == ModSourceType.Local && modAbout.WorkshopHandle == 0)
        publishButton.SetText("Publish");
      else if (modInfo.Source == ModSourceType.Workshop)
        publishButton.SetText("Unsubscribe");
      else
        publishButton.SetText("Update");

      var modIGDescription = modAbout.InGameDescription;
      __instance.DescriptionText.text = !string.IsNullOrEmpty(modIGDescription) ? modIGDescription : TextFormatting.SteamToTMP(modAbout.Description);
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

      workshopMenuSelectedField ??= typeof(WorkshopMenu).GetField("_selectedModItem", BindingFlags.Instance | BindingFlags.NonPublic);

      var selectedModItem = workshopMenuSelectedField.GetValue(WorkshopMenu.Instance) as WorkshopModListItem;
      if (selectedModItem == null)
        return;

      var modData = selectedModItem.Data;
      if (modData == null)
        return;

      ConfigPanel.DrawWorkshopConfig(LaunchPadConfig.MatchMod(modData));
    }
  }

  static class BugfixPatches
  {
    [HarmonyPatch(typeof(GameString), "op_Implicit", new Type[] { typeof(GameString) }), HarmonyPrefix]
    static bool GameString_op_Implicit(ref string __result, GameString gameString)
    {
      __result = gameString?.DisplayString ?? string.Empty;

      return false;
    }
  }

  static class CustomSavePathPatches
  {
    // This is initialized once when the config is loaded and not changed
    public static string SavePath;

    [HarmonyPatch(typeof(StationSaveUtils), nameof(StationSaveUtils.DefaultPath), MethodType.Getter), HarmonyPrefix]
    static bool StationSaveUtils_DefaultPath(ref string __result)
    {
      if (string.IsNullOrEmpty(SavePath))
        return true;

      if (Path.IsPathRooted(SavePath))
        __result = SavePath;
      else if (Platform.IsServer)
        __result = Path.Combine(StationSaveUtils.ExeDirectory.FullName, SavePath);
      else
        __result = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "My Games", SavePath);

      return false;
    }
  }

  static class LinuxPathPatch
  {
    [HarmonyPatch(typeof(WorldManager), "LoadDataFilesAtPath", typeof(string)), HarmonyTranspiler]
    static IEnumerable<CodeInstruction> WorldManager_LoadDataFilesAtPath(IEnumerable<CodeInstruction> instructions)
    {
      var matcher = new CodeMatcher(instructions);

      while (true)
      {
        matcher.MatchStartForward(new CodeInstruction(OpCodes.Ldstr));
        if (matcher.IsInvalid)
          break;
        var curStr = matcher.Instruction.operand as string;
        if (curStr.Contains("\\"))
        {
          var replaced = curStr.Replace('\\', Path.DirectorySeparatorChar);
          matcher.Instruction.operand = replaced;
          Logger.Global.LogDebug($"WorldManager.LoadDataFilesAtPath patched: {curStr} => {replaced}");
        }
        matcher.Advance(1);
      }

      return matcher.Instructions();
    }
  }
}