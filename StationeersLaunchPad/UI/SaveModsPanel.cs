using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Assets.Scripts.Serialization;
using Cysharp.Threading.Tasks;
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
  private const string FileName = "slp-mods.txt";
  private const string ObjectName = "SLP Save Mods";

  private const float DetailHeight = 380f;
  private const float Left = 680f;
  private const float Inset = 40f;
  private const float RightInset = 80f;
  private const float TitleSize = 24f;
  private const float RowSize = 20f;
  private const float RowSpacing = 6f;
  private const float GroupGap = 18f;
  private const float ColumnGap = 40f;
  private const int SingleColumnRows = 8;
  private const float MaxGroupColumnShare = 0.65f;
  private const int MaxRows = 300;

  private const string Ok = "#7CD992";
  private const string Missing = "#FF6B6B";
  private const string Disabled = "#FFA94D";
  private const string Dim = "#9AA0A6";
  private static readonly Color RowColor = new(0.86f, 0.87f, 0.89f);

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
    TextMeshProUGUI ____versionText, GameObject ____deleteButtonGameObject, FileInfo ____fileInfo)
  {
    try
    {
      if (____detailPanelRectTransform == null || ____versionText == null || ____fileInfo == null)
        return;
      if (____detailPanelRectTransform.Find(ObjectName) != null)
        return;
      Build(__instance, ____detailPanelRectTransform, ____versionText, ____deleteButtonGameObject, ____fileInfo);
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
      .Select(row => row.State is Slp2ModState.Older or Slp2ModState.Newer
        ? new Slp2ModRow(row.WorkshopHandle, row.Name, row.ServerVersion, row.LocalVersion, Slp2ModState.Match)
        : row)
      .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  private static void Build(SaveItem item, RectTransform detail, TextMeshProUGUI template, GameObject deleteButton, FileInfo saveFile)
  {
    var rows = ReadRows(saveFile, out var entries);
    if (rows == null)
      return;

    var container = new GameObject(ObjectName, typeof(RectTransform)).GetComponent<RectTransform>();
    container.SetParent(detail, false);
    container.anchorMin = new Vector2(0, 1);
    container.anchorMax = new Vector2(1, 1);
    container.pivot = new Vector2(0, 1);
    container.offsetMin = new Vector2(Left, 0);
    container.offsetMax = new Vector2(-RightInset, 0);
    container.anchoredPosition = new Vector2(container.anchoredPosition.x, -Inset);

    var titleHeight = LineHeight(template, TitleSize);
    var rowHeight = LineHeight(template, RowSize);
    var lines = BuildLines(rows, entries == null);
    var columns = lines.Count(line => line.Row != null) > SingleColumnRows ? 2 : 1;
    var parts = columns == 1 ? [lines] : SplitColumns(lines, titleHeight, rowHeight);

    var listHeight = 0f;
    for (var i = 0; i < parts.Count; i++)
    {
      var from = (float)i / columns;
      var to = (float)(i + 1) / columns;
      var padLeft = i > 0 ? ColumnGap / 2 : 0;
      var padRight = i < columns - 1 ? ColumnGap / 2 : 0;
      var y = 0f;
      foreach (var line in parts[i])
      {
        if (line.Gap)
        {
          y += GroupGap;
          continue;
        }
        var height = line.Title ? titleHeight : rowHeight;
        var text = CreateText(container, template, line.Title ? "Title" : "Mod");
        text.fontSize = line.Title ? TitleSize : RowSize;
        text.text = line.Text;
        if (line.Row is { } row)
        {
          text.color = RowColor;
          text.overflowMode = TextOverflowModes.Ellipsis;
          text.raycastTarget = true;
          text.gameObject.AddComponent<ModRowView>().Row = row;
        }
        Place(text, from, to, padLeft, padRight, y, height);
        y += height;
      }
      listHeight = Math.Max(listHeight, y);
    }

    container.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, listHeight);
    var view = container.gameObject.AddComponent<SaveModsView>();
    view.Extra = Math.Max(0f, Inset + listHeight + Inset - DetailHeight);

    if (entries == null)
      return;
    var state = item.gameObject.AddComponent<SaveModsState>();
    state.NotInstalled = rows.Count(row => row.State == Slp2ModState.NotInstalled);
    state.NotEnabled = rows.Count(row => row.State == Slp2ModState.NotEnabled);
    if (state.NotInstalled > 0 || state.NotEnabled > 0)
      AddUseButton(item, deleteButton, entries, state);
  }

  private sealed class SaveModsState : MonoBehaviour
  {
    public int NotInstalled;
    public int NotEnabled;
  }

  private static float LineHeight(TextMeshProUGUI template, float size)
  {
    var probe = UnityEngine.Object.Instantiate(template, template.transform.parent);
    probe.fontSize = size;
    probe.lineSpacing = 0;
    var height = probe.GetPreferredValues("Ag", 1000f, 0).y;
    UnityEngine.Object.DestroyImmediate(probe.gameObject);
    return height + RowSpacing;
  }

  private sealed class ModRowView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
  {
    public Slp2ModRow Row;
    private bool showing;

    public void OnPointerEnter(PointerEventData eventData)
    {
      GetComponent<TMP_Text>().color = Color.white;
      if (PanelToolTip.Instance == null)
        return;
      showing = true;
      var status = Row.State switch
      {
        Slp2ModState.NotInstalled => "Not installed.",
        Slp2ModState.NotEnabled => "Installed but not enabled.",
        _ => "Loaded.",
      };
      PanelToolTip.Instance.SetUpTooltip(DisplayName(Row), $"{status}\nClick to open its Workshop page.");
    }

    public void OnPointerExit(PointerEventData eventData) => Hide();

    private void OnDisable() => Hide();

    public void OnPointerClick(PointerEventData eventData) => Steam.OpenWorkshopPage(Row.WorkshopHandle);

    private void Hide()
    {
      GetComponent<TMP_Text>().color = RowColor;
      if (!showing)
        return;
      showing = false;
      PanelToolTip.Instance?.ClearToolTip();
    }
  }

  [HarmonyPatch(typeof(WorldItem), nameof(WorldItem.Expand)), HarmonyPostfix]
  private static void AddWorldSummary(WorldItem __instance, bool expand, TextMeshProUGUI ____infoText)
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

      if (state.NotInstalled > 0)
        ____infoText.text += $", <color={Missing}>{Plural(state.NotInstalled, "missing mod")}</color>";
      else if (state.NotEnabled > 0)
        ____infoText.text += $", <color={Disabled}>{Plural(state.NotEnabled, "mod")} not enabled</color>";
      else
        ____infoText.text += $", <color={Ok}>all mods loaded</color>";
    }
    catch (Exception ex)
    {
      Logger.Global.LogWarning("Could not check the world's mods");
      Logger.Global.LogException(ex);
    }
  }

  private sealed class HoverTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
  {
    public string Title;
    public string Body;
    private bool showing;

    public void OnPointerEnter(PointerEventData eventData)
    {
      if (PanelToolTip.Instance == null)
        return;
      showing = true;
      PanelToolTip.Instance.SetUpTooltip(Title, Body);
    }

    public void OnPointerExit(PointerEventData eventData) => Hide();

    private void OnDisable() => Hide();

    public void OnPointerClick(PointerEventData eventData) => Hide();

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
    text.lineSpacing = 0;
    text.richText = true;
    return text;
  }

  private static void Place(TextMeshProUGUI text, float from, float to, float padLeft, float padRight, float y, float height)
  {
    var rect = text.rectTransform;
    rect.anchorMin = new Vector2(from, 1);
    rect.anchorMax = new Vector2(to, 1);
    rect.pivot = new Vector2(0, 1);
    rect.offsetMin = new Vector2(padLeft, -y - height);
    rect.offsetMax = new Vector2(-padRight, -y);
  }

  private readonly struct Line(string text, Slp2ModRow? row = null, bool title = false, bool gap = false)
  {
    public readonly string Text = text;
    public readonly Slp2ModRow? Row = row;
    public readonly bool Title = title;
    public readonly bool Gap = gap;
  }

  private static readonly (string Title, string Color, Slp2ModState State)[] Groups =
  [
    ("Not installed", Missing, Slp2ModState.NotInstalled),
    ("Not enabled", Disabled, Slp2ModState.NotEnabled),
    ("Loaded", Ok, Slp2ModState.Match),
  ];

  private static List<Line> BuildLines(List<Slp2ModRow> rows, bool unreadable)
  {
    if (unreadable)
      return [new($"<color={Dim}>This save's mod list can't be read.</color>")];
    if (rows.Count == 0)
      return [new($"<color={Dim}>Made without mods.</color>")];

    var lines = new List<Line>();
    var left = MaxRows;
    foreach (var (title, color, state) in Groups)
    {
      var group = rows.Where(row => row.State == state).ToList();
      if (group.Count == 0)
        continue;
      if (lines.Count > 0)
        lines.Add(new("", gap: true));
      lines.Add(new($"<color={color}>{title}</color>  <color={Dim}>{group.Count}</color>", title: true));
      foreach (var row in group.Take(left))
        lines.Add(new(Escape(LaunchPadTag.Replace(DisplayName(row), "")), row));
      if (group.Count > left)
        lines.Add(new($"<color={Dim}>+{group.Count - left} more</color>"));
      left = Math.Max(0, left - group.Count);
    }
    return lines;
  }

  private static List<List<Line>> SplitColumns(List<Line> lines, float titleHeight, float rowHeight)
  {
    float Height(Line line) => line.Gap ? GroupGap : line.Title ? titleHeight : rowHeight;
    var total = lines.Sum(Height);
    var half = total / 2;

    var best = -1;
    var bestTaller = float.MaxValue;
    var above = 0f;
    for (var i = 0; i < lines.Count; i++)
    {
      if (lines[i].Gap)
      {
        var taller = Math.Max(above, total - above - GroupGap);
        if (taller < bestTaller)
          (best, bestTaller) = (i, taller);
      }
      above += Height(lines[i]);
    }
    if (best >= 0 && bestTaller <= total * MaxGroupColumnShare)
      return [lines.Take(best).ToList(), lines.Skip(best + 1).ToList()];

    var split = 0;
    for (var sum = 0f; split < lines.Count && sum + Height(lines[split]) / 2 <= half; split++)
      sum += Height(lines[split]);
    while (split > 1 && (lines[split - 1].Title || lines[split - 1].Gap))
      split--;
    var second = lines.Skip(split).SkipWhile(line => line.Gap).ToList();
    return [lines.Take(split).ToList(), second];
  }

  private static readonly Regex LaunchPadTag = new(@"\s*\[\s*StationeersLaunchPad\s*\]", RegexOptions.IgnoreCase);

  private static string DisplayName(Slp2ModRow row) =>
    string.IsNullOrWhiteSpace(row.Name) ? row.WorkshopHandle.ToString() : row.Name.Trim();

  private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

  private static void AddUseButton(SaveItem item, GameObject deleteButton, List<Slp2PackageCode.Entry> entries, SaveModsState state)
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
      label.text = state.NotInstalled > 0 ? $"Get {Plural(state.NotInstalled, "mod")}" : $"Enable {Plural(state.NotEnabled, "mod")}";
    }

    var button = copy.GetComponentInChildren<Button>();
    if (button == null)
      return;
    button.onClick.RemoveAllListeners();
    button.onClick.AddListener(() => ConfirmUse(item.WorldItem?.StationName, entries, state));
    var hover = button.gameObject.AddComponent<HoverTooltip>();
    hover.Title = "Play with this save's mods";
    hover.Body = "Switches to a pack with exactly the mods this save was made with and restarts the game. " +
      "Your clientside mods stay.";
  }

  private static void ConfirmUse(string worldName, List<Slp2PackageCode.Entry> entries, SaveModsState state)
  {
    var mods = entries.Where(entry => entry.WorkshopHandle > 1).ToList();
    var steps = new List<string>();
    if (state.NotInstalled > 0)
      steps.Add($"{Plural(state.NotInstalled, "mod")} will be downloaded from the Workshop on the next start.");
    if (state.NotEnabled > 0)
      steps.Add($"{Plural(state.NotEnabled, "installed mod")} will be enabled.");
    PromptPanel.Instance.ShowPrompt("Restart with this save's mods",
      $"SLP switches to a pack with the {Plural(mods.Count, "mod")} this save was made with and restarts the game. " +
      string.Join(" ", steps),
      "Restart", () => UseMods(worldName, mods));
  }

  // runs from the confirm prompt's button, which closes that prompt after its listeners
  private static async void ShowFailure(string message)
  {
    await UniTask.NextFrame();
    PromptPanel.Instance.ShowPrompt("Couldn't switch mods", message + " The log has the details.", "OK", () => { },
      hideCancelButton: true);
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
        ShowFailure("SLP couldn't create a pack from this save's mods.");
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
      ShowFailure("Something went wrong while switching to this save's mods.");
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
