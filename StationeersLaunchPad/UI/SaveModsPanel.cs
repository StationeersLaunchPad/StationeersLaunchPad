
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Assets.Scripts.Serialization;
using HarmonyLib;
using ICSharpCode.SharpZipLib.Zip;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Networking;
using StationeersLaunchPad.Sources;
using TMPro;
using UI.LoadGame;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using PanelToolTip = Assets.Scripts.UI.PanelToolTip;
using PromptPanel = Assets.Scripts.UI.PromptPanel;

namespace StationeersLaunchPad.UI;

[HarmonyPatch]
internal static class SaveModsPanel
{
  internal const string FileName = "slp-mods.txt";
  private const string ObjectName = "SLP Save Mods";

  private const float DetailHeight = 380f;
  private const float Left = 760f;
  private const float Inset = 40f;
  private const float RightInset = 80f;
  private const int MaxNameLength = 36;
  private const float RowScale = 0.72f;
  private const string TitleSize = "110%";
  private const float ColumnGap = 30f;
  private const int SingleColumnRows = 8;
  private const int MaxRows = 300;

  private const string Ok = "#7CD992";
  private const string Missing = "#FF6B6B";
  private const string Warn = "#FFC857";
  private const string Dim = "#9AA0A6";

  private static bool savedCodeBuilt;
  private static string savedCode;

  private sealed class SaveModsView : MonoBehaviour
  {
    public float Extra;
  }

  [HarmonyPatch(typeof(SaveHelper), "AddZipFile"), HarmonyPostfix]
  private static void WriteModList(ZipOutputStream zipStream, string fileName)
  {
    if (fileName != SaveLoadConstants.TerrainFileName)
      return;
    try
    {
      if (!savedCodeBuilt)
      {
        savedCode = BuildSavedCode();
        savedCodeBuilt = true;
      }
      if (savedCode == null)
        return;

      var bytes = Encoding.UTF8.GetBytes(savedCode);
      zipStream.PutNextEntry(new ZipEntry(FileName));
      zipStream.Write(bytes, 0, bytes.Length);
      zipStream.CloseEntry();
    }
    catch (Exception ex)
    {
      Logger.Global.LogWarning("Could not add the mod list to the save");
      Logger.Global.LogException(ex);
    }
  }

  private static string BuildSavedCode()
  {
    var mods = LaunchPadConfig.ModList.EnabledMods
      .Where(mod => mod.Source != ModSourceType.Core && !LaunchPadConfig.ProfileManager.IsClientside(mod))
      .ToList();
    var local = mods.FirstOrDefault(mod => mod.WorkshopHandle <= 1);
    if (local != null)
    {
      Logger.Global.LogInfo($"Saves won't carry a mod list, '{local.Name}' isn't on the workshop");
      return null;
    }
    return Slp2PackageCode.Encode(
      mods.Select(mod => new Slp2PackageCode.Entry(mod.WorkshopHandle, mod.Name, mod.About?.Version ?? "")),
      serverCode: false);
  }

  [HarmonyPatch(typeof(SaveItem), nameof(SaveItem.OnWorldItemExpanded)), HarmonyPostfix]
  private static void AddModList(SaveItem __instance, RectTransform ____detailPanelRectTransform,
    TextMeshProUGUI ____versionText, GameObject ____deleteButtonGameObject, Button ____loadButton, FileInfo ____fileInfo)
  {
    try
    {
      if (____detailPanelRectTransform == null || ____versionText == null || ____fileInfo == null)
        return;
      if (____detailPanelRectTransform.Find(ObjectName) != null)
        return;
      if (Build(__instance, ____detailPanelRectTransform, ____versionText, ____deleteButtonGameObject, ____fileInfo))
        AddMatchIcon(____loadButton,
          "This save was made with exactly the mods you have loaded right now.\nExpand it to see the list.");
    }
    catch (Exception ex)
    {
      Logger.Global.LogWarning("Could not show the save's mod list");
      Logger.Global.LogException(ex);
    }
  }

  [HarmonyPatch(typeof(SaveItem), nameof(SaveItem.CalculateSize)), HarmonyPostfix]
  private static void GrowForModList(SaveItem __instance, ref float __result, RectTransform ____detailPanelRectTransform)
  {
    if (!__instance.Expanded || ____detailPanelRectTransform == null)
      return;
    var view = ____detailPanelRectTransform.Find(ObjectName)?.GetComponent<SaveModsView>();
    if (view == null || view.Extra <= 0)
      return;

    ____detailPanelRectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, DetailHeight + view.Extra);
    __result += view.Extra;
    ((RectTransform)__instance.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, __result);
  }

  private static List<Slp2ModRow> ReadRows(FileInfo saveFile, out List<Slp2PackageCode.Entry> entries)
  {
    entries = null;
    var code = ReadCode(saveFile);
    if (code == null)
      return null;
    if (!Slp2PackageCode.TryDecode(code, out entries, out _, out _))
    {
      entries = null;
      return [];
    }
    return Slp2ModCompare.Compare(entries, LaunchPadConfig.ModList, LaunchPadConfig.ProfileManager)
      .Where(row => row.State != Slp2ModState.NotOnServer)
      .OrderBy(row => row.State == Slp2ModState.Match)
      .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  // true when the save's mods are exactly what's loaded
  private static bool Build(SaveItem item, RectTransform detail, TextMeshProUGUI template, GameObject deleteButton, FileInfo saveFile)
  {
    var rows = ReadRows(saveFile, out var entries);
    if (rows == null)
      return false;

    var container = new GameObject(ObjectName, typeof(RectTransform)).GetComponent<RectTransform>();
    container.SetParent(detail, false);
    container.anchorMin = new Vector2(0, 1);
    container.anchorMax = new Vector2(1, 1);
    container.pivot = new Vector2(0, 1);
    container.offsetMin = new Vector2(Left, 0);
    container.offsetMax = new Vector2(-RightInset, 0);
    container.anchoredPosition = new Vector2(container.anchoredPosition.x, -Inset);

    var width = detail.rect.width > Left + RightInset ? detail.rect.width - Left - RightInset : 400f;

    var header = CreateText(container, template, "Header");
    header.text = BuildHeader(rows, entries == null);
    var headerHeight = Place(header, 0, 1, 0, 0, 0, width);

    var lines = BuildLines(rows);

    var top = headerHeight + 10f;
    var listHeight = 0f;
    if (lines.Count > 0)
    {
      var columns = lines.Count > SingleColumnRows ? 2 : 1;
      var columnWidth = (width - ColumnGap * (columns - 1)) / columns;
      var parts = columns == 1 ? [lines] : SplitColumns(lines);
      for (var i = 0; i < parts.Count; i++)
      {
        var column = CreateText(container, template, $"Column {i + 1}");
        // a size per line throws off the measured height
        column.fontSize = template.fontSize * RowScale;
        column.text = string.Join("\n", parts[i].Select(line => line.Text));
        listHeight = Math.Max(listHeight, Place(column, (float)i / columns, (float)(i + 1) / columns,
          i > 0 ? ColumnGap / 2 : 0, i < columns - 1 ? ColumnGap / 2 : 0, top, columnWidth));
      }
    }

    var contentHeight = top + listHeight;
    container.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentHeight);
    var view = container.gameObject.AddComponent<SaveModsView>();
    view.Extra = Math.Max(0f, Inset + contentHeight + Inset - DetailHeight);

    if (entries == null)
      return false;
    var state = item.gameObject.AddComponent<SaveModsState>();
    state.Missing = rows.Count(row => row.State is Slp2ModState.NotInstalled or Slp2ModState.NotEnabled);
    state.Differ = rows.Count(row => row.State is Slp2ModState.Older or Slp2ModState.Newer);
    var matches = state.Missing == 0 && state.Differ == 0;
    if (!matches)
      AddUseButton(item, deleteButton, entries);
    return matches;
  }

  private sealed class SaveModsState : MonoBehaviour
  {
    public int Missing;
    public int Differ;
  }

  [HarmonyPatch(typeof(WorldItem), nameof(WorldItem.Expand)), HarmonyPostfix]
  private static void AddWorldSummary(WorldItem __instance, bool expand, TextMeshProUGUI ____infoText, Button ____loadLatestButton)
  {
    try
    {
      if (!expand || ____infoText == null || ____infoText.transform.Find(ObjectName) != null)
        return;
      // saves without a preview image, like a dedicated server's, stop the game's read before the date
      foreach (var item in __instance.SaveItems.Where(item => item.SaveDateTime == default))
        item.LoadSaveDateTimeOnly();
      var latest = __instance.SaveItems.OrderByDescending(item => item.SaveDateTime).FirstOrDefault();
      var state = latest == null ? null : latest.GetComponent<SaveModsState>();
      if (state == null)
        return;
      new GameObject(ObjectName, typeof(RectTransform)).transform.SetParent(____infoText.transform, false);

      if (state.Missing > 0)
        ____infoText.text += $", <color={Missing}>{state.Missing} missing mod{(state.Missing == 1 ? "" : "s")}</color>";
      else if (state.Differ > 0)
        ____infoText.text += $", <color={Warn}>{state.Differ} mod{(state.Differ == 1 ? "" : "s")} on another version</color>";
      else
        AddMatchIcon(____loadLatestButton, "The latest save was made with exactly the mods you have loaded right now.");
    }
    catch (Exception ex)
    {
      Logger.Global.LogWarning("Could not check the world's mods");
      Logger.Global.LogException(ex);
    }
  }

  private static void AddMatchIcon(Button loadButton, string tooltip)
  {
    if (loadButton == null || loadButton.transform.Find(ObjectName + " Match") != null)
      return;
    var texture = ModImages.GetBuiltInTexture(ModImages.TrustedImage);
    if (texture == null)
      return;

    var icon = new GameObject(ObjectName + " Match", typeof(RectTransform), typeof(RawImage), typeof(MatchIcon));
    var rect = (RectTransform)icon.transform;
    rect.SetParent(loadButton.transform, false);
    rect.anchorMin = rect.anchorMax = new Vector2(0, 0.5f);
    rect.pivot = new Vector2(1, 0.5f);
    rect.anchoredPosition = new Vector2(-12, 0);
    rect.sizeDelta = new Vector2(30, 30);
    icon.GetComponent<RawImage>().texture = texture;
    icon.GetComponent<MatchIcon>().Tooltip = tooltip;
  }

  private sealed class MatchIcon : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
  {
    public string Tooltip;
    private bool showing;

    public void OnPointerEnter(PointerEventData eventData)
    {
      if (PanelToolTip.Instance == null)
        return;
      showing = true;
      PanelToolTip.Instance.SetUpTooltip("Mods match", Tooltip);
    }

    public void OnPointerExit(PointerEventData eventData) => Hide();

    private void OnDisable() => Hide();

    // keeps the click from reaching the Load button
    public void OnPointerClick(PointerEventData eventData) { }

    private void Hide()
    {
      if (!showing)
        return;
      showing = false;
      PanelToolTip.Instance?.ClearToolTip();
    }
  }

  private static TextMeshProUGUI CreateText(RectTransform parent, TextMeshProUGUI template, string name)
  {
    var text = UnityEngine.Object.Instantiate(template, parent);
    text.name = name;
    text.raycastTarget = false;
    text.alignment = TextAlignmentOptions.TopLeft;
    text.enableWordWrapping = false;
    text.overflowMode = TextOverflowModes.Overflow;
    text.richText = true;
    return text;
  }

  // returns the height the text needs
  private static float Place(TextMeshProUGUI text, float from, float to, float padLeft, float padRight, float y, float measureWidth)
  {
    var height = text.GetPreferredValues(text.text, measureWidth, 0).y;
    var rect = text.rectTransform;
    rect.anchorMin = new Vector2(from, 1);
    rect.anchorMax = new Vector2(to, 1);
    rect.pivot = new Vector2(0, 1);
    rect.offsetMin = new Vector2(padLeft, -y - height);
    rect.offsetMax = new Vector2(-padRight, -y);
    return height;
  }

  private static string BuildHeader(List<Slp2ModRow> rows, bool unreadable)
  {
    if (unreadable)
      return $"Mods\n<size=75%><color={Dim}>unreadable mod list</color></size>";
    if (rows.Count == 0)
      return $"Mods\n<size=75%><color={Dim}>no mods</color></size>";
    return "Mods";
  }

  private readonly struct Line(string text, bool groupTitle)
  {
    public readonly string Text = text;
    public readonly bool GroupTitle = groupTitle;
  }

  private static readonly (string Title, string Color, Slp2ModState[] States)[] Groups =
  [
    ("Missing", Missing, [Slp2ModState.NotInstalled, Slp2ModState.NotEnabled]),
    ("Different version", Warn, [Slp2ModState.Older, Slp2ModState.Newer]),
    ("Loaded", Ok, [Slp2ModState.Match]),
  ];

  private static List<Line> BuildLines(List<Slp2ModRow> rows)
  {
    var lines = new List<Line>();
    var left = MaxRows;
    foreach (var (title, color, states) in Groups)
    {
      var group = rows.Where(row => states.Contains(row.State)).ToList();
      if (group.Count == 0)
        continue;
      // the gap counts as part of the title, so a column split keeps them together
      if (lines.Count > 0)
        lines.Add(new("<size=50%> </size>", true));
      lines.Add(new($"<size={TitleSize}><color={color}>{title}</color>  <color={Dim}>{group.Count}</color></size>", true));
      foreach (var row in group.Take(left))
        lines.Add(new($"  {FormatRow(row)}", false));
      if (group.Count > left)
        lines.Add(new($"  <color={Dim}>+{group.Count - left} more</color>", false));
      left = Math.Max(0, left - group.Count);
    }
    return lines;
  }

  // a group title never ends a column
  private static List<List<Line>> SplitColumns(List<Line> lines)
  {
    var split = (lines.Count + 1) / 2;
    while (split > 1 && lines[split - 1].GroupTitle)
      split--;
    var second = lines.Skip(split).ToList();
    if (second.Count > 1 && second[0].GroupTitle && second[1].GroupTitle)
      second.RemoveAt(0);
    return [lines.Take(split).ToList(), second];
  }

  private static string FormatRow(Slp2ModRow row)
  {
    var name = string.IsNullOrEmpty(row.Name) ? row.WorkshopHandle.ToString() : row.Name.Trim();
    if (name.Length > MaxNameLength)
      name = name[..(MaxNameLength - 3)].TrimEnd() + "...";
    return Escape(name);
  }

  private static void AddUseButton(SaveItem item, GameObject deleteButton, List<Slp2PackageCode.Entry> entries)
  {
    if (deleteButton == null)
      return;
    var copy = UnityEngine.Object.Instantiate(deleteButton, deleteButton.transform.parent);
    copy.name = ObjectName + " Button";
    copy.SetActive(true);
    copy.transform.SetSiblingIndex(0);
    var rect = (RectTransform)copy.transform;
    rect.sizeDelta = new Vector2(260, rect.sizeDelta.y);
    var label = copy.GetComponentInChildren<TextMeshProUGUI>();
    if (label != null)
    {
      // the label's localizer would put "Delete" back
      foreach (var behaviour in label.GetComponents<MonoBehaviour>())
        if (behaviour is not TMP_Text)
          UnityEngine.Object.DestroyImmediate(behaviour);
      label.text = "Use these mods";
    }

    var button = copy.GetComponentInChildren<Button>();
    if (button == null)
      return;
    button.onClick.RemoveAllListeners();
    button.onClick.AddListener(() => ConfirmUse(item.WorldItem?.StationName, entries));
  }

  private static void ConfirmUse(string worldName, List<Slp2PackageCode.Entry> entries)
  {
    var mods = entries.Where(entry => entry.WorkshopHandle > 1).ToList();
    PromptPanel.Instance.ShowPrompt("Restart required",
      $"SLP switches to a pack with this save's {mods.Count} mods and restarts the game. " +
      "Missing Workshop mods are downloaded on the next start.",
      "Restart", () => UseMods(worldName, mods));
  }

  private static void UseMods(string worldName, List<Slp2PackageCode.Entry> mods)
  {
    try
    {
      var manager = LaunchPadConfig.ProfileManager;
      var handles = new HashSet<ulong>(mods.Select(mod => mod.WorkshopHandle));

      var pack = manager.UserPacks.FirstOrDefault(profile =>
        profile.Mods.Count == handles.Count && profile.Mods.All(mod => handles.Contains(mod.WorkshopHandle)));
      if (pack == null)
      {
        var name = manager.UniqueName(worldName, "Save");
        var created = manager.CreatePack(name, mods.Select(mod => new ProfileModEntry
        {
          Name = mod.Name,
          Source = ModSourceType.Workshop,
          WorkshopHandle = mod.WorkshopHandle,
        }));
        pack = created ? manager.FindProfile(name) : null;
      }
      if (pack == null)
      {
        Logger.Global.LogError("Could not create a pack from the save's mods");
        return;
      }

      // takes effect on the next start
      Configs.ModProfile.Value = pack.Name;
      ProcessUtil.RestartGame();
    }
    catch (Exception ex)
    {
      Logger.Global.LogError("Could not switch to the save's mods");
      Logger.Global.LogException(ex);
    }
  }

  private static string ReadCode(FileInfo saveFile)
  {
    using var stream = File.OpenRead(saveFile.FullName);
    using var zip = new ZipFile(stream);
    var entry = zip.GetEntry(FileName);
    if (entry == null)
      return null;
    using var reader = new StreamReader(zip.GetInputStream(entry));
    return reader.ReadToEnd().Trim();
  }

  private static string Escape(string value) => value?.Replace("<", "<​") ?? "";
}
