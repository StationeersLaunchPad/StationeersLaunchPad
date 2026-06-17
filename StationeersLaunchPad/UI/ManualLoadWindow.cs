using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using StationeersLaunchPad.Loading;
using StationeersLaunchPad.Metadata;
using StationeersLaunchPad.Sources;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public static class ManualLoadWindow
{
  [Flags]
  public enum ChangeFlags
  {
    None = 0,
    Mods = 1 << 0,
    AutoSort = 1 << 1,
    NextStep = 1 << 2,
  }

  private static LoadStage lastStage;
  private static ModInfo selectedInfo = null;
  private static LoadedMod selectedMod = null;
  private static bool openInfo = false;
  private static ModInfo draggingMod = null;
  private static bool dragged = false;

  // Mod list sorting/filtering state (see DrawModListTools / DrawSearchAndCount)
  private static ModList.ModSortField sortField = ModList.ModSortField.LoadOrder;
  private static bool sortDescending = false;
  private static bool sortApplyToLoadOrder = false;
  private static string searchText = "";
  private static int presetIndex = 0;
  private static string presetNameInput = "";
  private static bool authorSplitDrag = false;
  // Redesigned shell: left-sidebar navigation sections + collapsible console.
  private enum NavSection { Mods, Dependencies, Settings, About }
  private static NavSection nav = NavSection.Mods;
  private static bool consoleOpen = true;
  private static bool layoutDirty = false;

  private static readonly string[] SortFieldLabels =
    { "Load order", "Name", "Author", "Released", "Updated" };

  // Subtle background tint for alternating list rows (zebra striping).
  private static uint RowAltColor =>
    ImGui.ColorConvertFloat4ToU32(new Color(1f, 1f, 1f, 0.045f));

  public static ChangeFlags Draw(LoadStage stage, ModList modList, bool autoSort)
  {
    Platform.SetBackgroundEnabled(false);
    var changed = ChangeFlags.None;

    // when we move into the loading step, clear the selected mod so all the logs are visible
    if (lastStage < LoadStage.Loaded && stage >= LoadStage.Loading)
      selectedInfo = null;
    lastStage = stage;

    ImGuiHelper.Draw(() =>
    {
      LaunchPadTheme.Push();

      var windowRect = ImGuiHelper.ScreenRect().Shrink(25f);
      ImGuiHelper.SetNextWindowRect(windowRect);
      ImGui.Begin("##preloadermanual", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);

      var full = ImGuiHelper.AvailableRect();
      LaunchPadTheme.Fill(full, LaunchPadTheme.Bg);

      full.SplitOY(36f, out var titleRect, out var bodyRect);
      DrawTitleBar(titleRect, stage, modList);

      bodyRect.SplitOX(210f, out var sideRect, out var mainRect);
      if (DrawSidebar(sideRect, stage))
        changed |= ChangeFlags.NextStep;

      var winHeight = full.Size.y;
      float consoleHeight;
      if (consoleOpen)
      {
        var minConsole = ImGui.GetTextLineHeightWithSpacing() * 8f + 44f;
        consoleHeight = Mathf.Clamp(winHeight * LayoutPrefs.Current.ConsoleFraction,
          minConsole, mainRect.Size.y * 0.65f);
      }
      else
        consoleHeight = 26f;

      mainRect.SplitOY(-consoleHeight, out var contentRect, out var consoleRect);

      var splitterRect = default(Rect);
      if (consoleOpen)
        contentRect.SplitOY(-5f, out contentRect, out splitterRect);

      changed |= DrawMainContent(contentRect, stage, modList, autoSort);

      if (consoleOpen)
      {
        var dy = HSplitter("##consolesplit", splitterRect);
        if (dy != 0f)
          LayoutPrefs.Current.ConsoleFraction =
            Mathf.Clamp(LayoutPrefs.Current.ConsoleFraction - dy / winHeight, 0.12f, 0.55f);
      }

      DrawConsolePanel(consoleRect);

      ImGui.End();
      LaunchPadTheme.Pop();
    });
    return changed;
  }

  // -- Redesigned shell -------------------------------------------

  private static void DrawTitleBar(Rect rect, LoadStage stage, ModList modList)
  {
    LaunchPadTheme.Fill(rect, LaunchPadTheme.Deep);
    LaunchPadTheme.HLine(rect.BL, rect.BR, LaunchPadTheme.Border);

    var cy = rect.Min.y + (rect.Size.y - ImGui.GetTextLineHeight()) / 2f;
    const float pad = 14f;

    var logo = new Rect(new(rect.Min.x + pad, rect.Min.y + 8f), new(rect.Min.x + pad + 20f, rect.Min.y + 28f));
    LaunchPadTheme.Fill(logo, LaunchPadTheme.OrangeFaint);
    LaunchPadTheme.Stroke(logo, LaunchPadTheme.OrangeBorder);
    LaunchPadTheme.TextAt(new(logo.Min.x + 6f, cy), "L", LaunchPadTheme.Orange);

    var x = rect.Min.x + pad + 30f;
    LaunchPadTheme.TextAt(new(x, cy), "STATIONEERS LAUNCHPAD", LaunchPadTheme.Text);
    x += ImGui.CalcTextSize("STATIONEERS LAUNCHPAD").x + 10f;
    LaunchPadTheme.TextAt(new(x, cy), $"v{LaunchPadInfo.VERSION}", LaunchPadTheme.TextMuted);

    var mods = modList.AllMods.Where(m => m.Source != ModSourceType.Core).ToList();
    var enabled = mods.Count(m => m.Enabled);
    var disabled = mods.Count - enabled;
    var errs = PreLoadCheck.Current?.Errors ?? 0;
    var warns = PreLoadCheck.Current?.Warnings ?? 0;

    var segs = new List<(string, Color)>
    {
      (StageText(stage), LaunchPadTheme.TextSub),
      ($"Active {enabled}", LaunchPadTheme.Orange),
      ($"Disabled {disabled}", LaunchPadTheme.TextSub),
      ($"Warn {warns}", warns > 0 ? LaunchPadTheme.Warn : LaunchPadTheme.TextDim),
      ($"Err {errs}", errs > 0 ? LaunchPadTheme.Err : LaunchPadTheme.TextDim),
    };

    var width = segs.Sum(s => ImGui.CalcTextSize(s.Item1).x + 16f) - 16f;
    var sx = rect.Max.x - pad - width;
    foreach (var (text, color) in segs)
    {
      LaunchPadTheme.TextAt(new(sx, cy), text, color);
      sx += ImGui.CalcTextSize(text).x + 16f;
    }
  }

  private static string StageText(LoadStage stage) => stage switch
  {
    LoadStage.Updating => "Checking for updates",
    LoadStage.Initializing => "Initializing",
    LoadStage.Searching => "Locating mods",
    LoadStage.Configuring => "Ready to load mods",
    LoadStage.Loading => "Loading mods",
    LoadStage.Loaded => "Ready to start game",
    LoadStage.Failed => "Mods failed to load",
    _ => "",
  };

  private static bool DrawSidebar(Rect rect, LoadStage stage)
  {
    LaunchPadTheme.Fill(rect, LaunchPadTheme.Sidebar);
    LaunchPadTheme.HLine(rect.TR, rect.BR, LaunchPadTheme.Border);

    const float pad = 10f;
    var x = rect.Min.x + pad;
    var w = rect.Size.x - pad * 2f;
    var y = rect.Min.y + 14f;

    LaunchPadTheme.TextAt(new(x + 4f, y), "NAVIGATION", LaunchPadTheme.TextDim);
    y += 22f;

    (NavSection id, string label)[] items =
    {
      (NavSection.Mods, "Mod Manager"),
      (NavSection.Dependencies, "Dependency Tree"),
      (NavSection.Settings, "Configuration"),
      (NavSection.About, "About"),
    };
    foreach (var (id, label) in items)
    {
      ImGui.SetCursorScreenPos(new(x, y));
      var active = nav == id;
      if (active)
        ImGui.PushStyleColor(ImGuiCol.Text, (Vector4)LaunchPadTheme.Orange);
      if (ImGui.Selectable($"  {label}", active, new Vector2(w, 26f)))
        nav = id;
      if (active)
        ImGui.PopStyleColor();
      y += 28f;
    }

    y += 8f;
    LaunchPadTheme.HLine(new(x, y), new(x + w, y), LaunchPadTheme.Border);
    y += 10f;

    var (loadEnabled, loadText) = stage switch
    {
      LoadStage.Configuring => (true, "Load Mods"),
      LoadStage.Loaded or LoadStage.Failed => (true, "Start Game"),
      _ => (false, "..."),
    };

    ImGui.SetCursorScreenPos(new(x, y));
    ImGui.PushStyleColor(ImGuiCol.Button, (Vector4)(loadEnabled ? LaunchPadTheme.Orange : new Color(1f, 1f, 1f, 0.04f)));
    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, (Vector4)(loadEnabled ? LaunchPadTheme.Hex(0xE06B1F) : new Color(1f, 1f, 1f, 0.06f)));
    ImGui.PushStyleColor(ImGuiCol.ButtonActive, (Vector4)LaunchPadTheme.Orange);
    ImGui.PushStyleColor(ImGuiCol.Text, (Vector4)(loadEnabled ? Color.white : LaunchPadTheme.TextMuted));
    ImGui.BeginDisabled(!loadEnabled);
    var clicked = ImGui.Button(loadText, new Vector2(w, 32f));
    ImGui.EndDisabled();
    ImGui.PopStyleColor(4);

    return clicked;
  }

  private static ChangeFlags DrawMainContent(Rect rect, LoadStage stage, ModList modList, bool autoSort)
  {
    var changed = ChangeFlags.None;
    ImGui.SetCursorScreenPos(rect.Min);
    ImGui.BeginChild("##maincontent", rect.Size);

    switch (nav)
    {
      case NavSection.Mods:
        changed |= DrawModsView(rect, stage, modList, autoSort);
        break;
      case NavSection.Dependencies:
        if (DrawModTree(modList))
          changed |= ChangeFlags.Mods;
        break;
      case NavSection.Settings:
        if (ConfigPanel.DrawConfigFile(Configs.Sorted, category => category != "Internal")
            && stage <= LoadStage.Configuring)
          changed |= ChangeFlags.Mods;
        break;
      case NavSection.About:
        DrawAboutView();
        break;
    }

    ImGui.EndChild();
    return changed;
  }

  private static ChangeFlags DrawModsView(Rect rect, LoadStage stage, ModList modList, bool autoSort)
  {
    var changed = ChangeFlags.None;

    if (stage is LoadStage.Searching or LoadStage.Configuring)
    {
      var (flagged, checkChanged) = PreLoadCheckPanel.DrawBanner(modList);
      if (checkChanged)
        changed |= ChangeFlags.Mods;
      if (flagged != null && flagged.Source != ModSourceType.Core)
      {
        selectedInfo = flagged;
        selectedMod = null;
      }
      changed |= DrawModSelectOptions(modList, autoSort);
    }
    else
    {
      DrawModListToolbar(modList, loaded: true);
      ImGui.Separator();
    }

    var area = ImGuiHelper.AvailableRect();
    var listW = Mathf.Clamp(LayoutPrefs.Current.ListWidth, 260f, area.Size.x - 300f);
    area.SplitOX(listW, out var listRect, out var rest);
    rest.SplitOX(5f, out var listSplitter, out var detailRect);

    ImGui.SetCursorScreenPos(listRect.Min);
    ImGui.BeginChild("##modlist", listRect.Size);
    if (stage is LoadStage.Searching or LoadStage.Configuring)
    {
      if (DrawModSelectTable(modList, stage == LoadStage.Configuring, autoSort))
        changed |= ChangeFlags.Mods;
    }
    else
      DrawLoadTable(modList);
    ImGui.EndChild();

    var dx = VSplitter("##listsplit", listSplitter);
    if (dx != 0f)
      LayoutPrefs.Current.ListWidth = Mathf.Clamp(listW + dx, 260f, area.Size.x - 300f);

    LaunchPadTheme.Fill(detailRect, LaunchPadTheme.PanelAlt);
    LaunchPadTheme.HLine(detailRect.TL, detailRect.BL, LaunchPadTheme.Border);
    var inner = detailRect.Shrink(12f);
    ImGui.SetCursorScreenPos(inner.Min);
    ImGui.BeginChild("##detail", inner.Size, false, ImGuiWindowFlags.HorizontalScrollbar);
    DrawModDetail(modList, selectedInfo);
    ImGui.EndChild();

    return changed;
  }

  private static void DrawAboutView()
  {
    ImGuiHelper.TextColored("Stationeers LaunchPad", LaunchPadTheme.Orange);
    ImGuiHelper.TextDisabled($"Version {LaunchPadInfo.VERSION}");
    ImGui.Separator();
    ImGuiHelper.Text("Community mod loader for Stationeers.");
    ImGuiHelper.TextDisabled("Unofficial community tool, not affiliated with RocketWerkz.");
  }

  // -- Draggable splitters (persisted via LayoutPrefs) ------------
  private static float VSplitter(string id, Rect rect)
  {
    ImGui.SetCursorScreenPos(rect.Min);
    ImGui.InvisibleButton(id, rect.Size);
    var active = ImGui.IsItemActive();
    var hovered = ImGui.IsItemHovered();
    if (active || hovered)
      ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
    var cx = rect.Min.x + rect.Size.x / 2f;
    LaunchPadTheme.HLine(new(cx, rect.Min.y + 2f), new(cx, rect.Max.y - 2f),
      active ? LaunchPadTheme.Orange : hovered ? LaunchPadTheme.OrangeBorder : LaunchPadTheme.Border);
    return SplitterDelta(active, ImGui.GetIO().MouseDelta.x);
  }

  private static float HSplitter(string id, Rect rect)
  {
    ImGui.SetCursorScreenPos(rect.Min);
    ImGui.InvisibleButton(id, rect.Size);
    var active = ImGui.IsItemActive();
    var hovered = ImGui.IsItemHovered();
    if (active || hovered)
      ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNS);
    var cy = rect.Min.y + rect.Size.y / 2f;
    LaunchPadTheme.HLine(new(rect.Min.x + 2f, cy), new(rect.Max.x - 2f, cy),
      active ? LaunchPadTheme.Orange : hovered ? LaunchPadTheme.OrangeBorder : LaunchPadTheme.Border);
    return SplitterDelta(active, ImGui.GetIO().MouseDelta.y);
  }

  private static float SplitterDelta(bool active, float delta)
  {
    if (active)
    {
      layoutDirty = true;
      return delta;
    }
    if (layoutDirty && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
    {
      LayoutPrefs.Save();
      layoutDirty = false;
    }
    return 0f;
  }

  // -- Status system ----------------------------------------------
  private enum ModUiStatus { Ok, Warn, Error, Disabled }

  // Worst pre-load-check severity (Warning/Error) per mod, for status icons/chips.
  private static Dictionary<ModInfo, CheckSeverity> BuildSeverity()
  {
    var dict = new Dictionary<ModInfo, CheckSeverity>();
    var issues = PreLoadCheck.Current?.Issues;
    if (issues != null)
      foreach (var i in issues)
        if (i.Mod != null && i.Severity != CheckSeverity.Info
            && (!dict.TryGetValue(i.Mod, out var cur) || i.Severity > cur))
          dict[i.Mod] = i.Severity;
    return dict;
  }

  private static ModUiStatus StatusOf(ModInfo mod, Dictionary<ModInfo, CheckSeverity> sev)
  {
    if (!mod.Enabled && mod.Source != ModSourceType.Core)
      return ModUiStatus.Disabled;
    if (sev.TryGetValue(mod, out var s))
      return s == CheckSeverity.Error ? ModUiStatus.Error : ModUiStatus.Warn;
    return ModUiStatus.Ok;
  }

  private static Color StatusColor(ModUiStatus s) => s switch
  {
    ModUiStatus.Ok => LaunchPadTheme.Ok,
    ModUiStatus.Warn => LaunchPadTheme.Warn,
    ModUiStatus.Error => LaunchPadTheme.Err,
    _ => LaunchPadTheme.TextMuted,
  };

  private static void DrawStatusDot(Rect rect, ModUiStatus status)
  {
    var center = (rect.Min + rect.Max) / 2f;
    ImGui.GetWindowDrawList().AddCircleFilled(center, 4f,
      ImGui.ColorConvertFloat4ToU32((Vector4)StatusColor(status)));
  }

  private static void DrawStatusChip(ModUiStatus status)
  {
    var label = status switch
    {
      ModUiStatus.Ok => "OK",
      ModUiStatus.Warn => "WARN",
      ModUiStatus.Error => "ERR",
      _ => "OFF",
    };
    ImGuiHelper.TextColored(label, StatusColor(status));
  }

  // -- Redesigned detail panel ------------------------------------
  private static void DrawModDetail(ModList modList, ModInfo mod)
  {
    if (mod == null)
    {
      ImGuiHelper.TextDisabled("Select a mod to view details");
      return;
    }

    var about = mod.About;
    var sev = BuildSeverity();

    ImGui.AlignTextToFramePadding();
    ImGuiHelper.TextColored(mod.Name, LaunchPadTheme.Text);
    ImGui.SameLine();
    DrawStatusChip(StatusOf(mod, sev));

    ImGuiHelper.TextDisabled($"by {(string.IsNullOrEmpty(mod.Author) ? "Unknown" : mod.Author)}");
    if (mod.WorkshopHandle > 1)
    {
      ImGui.SameLine();
      ImGuiHelper.TextDisabled($"#{mod.WorkshopHandle}");
    }

    ImGui.Separator();

    DetailField("Version", string.IsNullOrEmpty(about?.Version) ? "-" : about.Version.Trim());
    DetailField("Updated", mod.Updated?.ToString("yyyy-MM-dd") ?? "-");
    DetailField("Source", mod.Source.ToString());

    ImGui.Separator();

    ImGuiHelper.TextDisabled("DESCRIPTION");
    var desc = about?.Description;
    ImGuiHelper.TextWrapped(string.IsNullOrEmpty(desc) ? "No description provided." : StripBBCode(desc));

    if (about?.Tags is { Count: > 0 } tags)
    {
      ImGui.Spacing();
      for (var i = 0; i < tags.Count; i++)
      {
        if (i > 0)
          ImGui.SameLine();
        ImGuiHelper.TextDisabled($"[{tags[i]}]");
      }
    }

    var deps = new List<ModInfo>();
    foreach (var d in about?.DependsOn ?? [])
    {
      if (!d.IsValid)
        continue;
      var p = modList.AllMods.FirstOrDefault(m => m != mod && m.Satisfies(d));
      if (p != null)
        deps.Add(p);
    }
    if (deps.Count > 0)
    {
      ImGui.Separator();
      ImGuiHelper.TextDisabled($"REQUIRES ({deps.Count})");
      foreach (var dep in deps)
        if (DrawDepRow(dep, sev))
          selectedInfo = dep;
    }

    var dependents = modList.AllMods
      .Where(m => m != mod && (m.About?.DependsOn?.Any(d => d.IsValid && mod.Satisfies(d)) ?? false))
      .ToList();
    if (dependents.Count > 0)
    {
      ImGui.Separator();
      ImGuiHelper.TextDisabled($"REQUIRED BY ({dependents.Count})");
      foreach (var dep in dependents)
        if (DrawDepRow(dep, sev))
          selectedInfo = dep;
    }

    ImGui.Separator();
    if (mod.Source != ModSourceType.Core)
    {
      if (ImGui.Button(mod.Enabled ? "Disable" : "Enable"))
        mod.Enabled = !mod.Enabled;
      ImGui.SameLine();
    }
    if (!string.IsNullOrEmpty(mod.DirectoryPath))
    {
      if (ImGui.Button("Open Folder"))
        ProcessUtil.OpenExplorerDir(mod.DirectoryPath);
      ImGui.SameLine();
    }
    if (mod.WorkshopHandle > 1 && ImGui.Button("Open Workshop Page"))
      Steam.OpenWorkshopPage(mod.WorkshopHandle);
  }

  // Strips Steam Workshop BBCode tags ([h1], [b], [url=...], [list], etc.) for readability.
  private static string StripBBCode(string text) =>
    System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\[/?[^\]]*\]", "").Trim();

  private static void DetailField(string label, string value)
  {
    ImGuiHelper.TextDisabled(label);
    ImGui.SameLine(110f);
    ImGuiHelper.Text(value);
  }

  private static bool DrawDepRow(ModInfo mod, Dictionary<ModInfo, CheckSeverity> sev)
  {
    var pos = ImGui.GetCursorScreenPos();
    var h = ImGui.GetTextLineHeightWithSpacing();
    var clicked = ImGui.Button($"##dep_{mod.DirectoryName}", new Vector2(ImGui.GetContentRegionAvail().x, h));

    // Draw the dot + name on top of the button via the draw list (no cursor changes).
    var dl = ImGui.GetWindowDrawList();
    dl.AddCircleFilled(new(pos.x + 9f, pos.y + h / 2f), 4f,
      ImGui.ColorConvertFloat4ToU32((Vector4)StatusColor(StatusOf(mod, sev))));
    dl.AddText(new(pos.x + 22f, pos.y + (h - ImGui.GetTextLineHeight()) / 2f),
      ImGui.ColorConvertFloat4ToU32((Vector4)LaunchPadTheme.Text), mod.Name);
    return clicked;
  }

  private static void DrawConsolePanel(Rect rect)
  {
    LaunchPadTheme.Fill(rect, LaunchPadTheme.Deep);
    LaunchPadTheme.HLine(rect.TL, rect.TR, LaunchPadTheme.Border);

    rect.SplitOY(26f, out var headerRect, out var bodyRect);

    ImGui.SetCursorScreenPos(headerRect.Min);
    if (ImGui.InvisibleButton("##consoletoggle", headerRect.Size))
      consoleOpen = !consoleOpen;
    var cy = headerRect.Min.y + (headerRect.Size.y - ImGui.GetTextLineHeight()) / 2f;
    LaunchPadTheme.TextAt(new(headerRect.Min.x + 10f, cy), consoleOpen ? "v CONSOLE" : "> CONSOLE", LaunchPadTheme.TextMuted);

    if (!consoleOpen)
      return;

    bodyRect.SplitOY(-ImGui.GetTextLineHeightWithSpacing(), out var logRect, out var inputRect);

    ImGui.SetCursorScreenPos(logRect.Min);
    ImGui.BeginChild("##logs", logRect.Size);
    LogPanel.DrawConsole(selectedMod?.Logger ?? Logger.Global);
    ImGui.EndChild();

    StartupConsole.DrawInput(inputRect);
  }

  private static bool DrawStatusLine(Rect rect, LoadStage stage)
  {
    var next = false;
    ImGui.SetCursorScreenPos(rect.Min);

    ImGuiHelper.TextDisabled(LaunchPadInfo.VERSION);
    ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextDisabled("|"), true);

    ImGuiHelper.Text(stage switch
    {
      LoadStage.Updating => "Checking for updates to StationeersLaunchPad",
      LoadStage.Initializing => "Initializing core components",
      LoadStage.Searching => "Locating installed local and workshop mods",
      LoadStage.Configuring => "Ready to load mods",
      LoadStage.Loading => "Loading selected mods",
      LoadStage.Loaded => "Ready to start game",
      LoadStage.Failed => "Mods failed to load. Game may not function properly",
      _ => "",
    });

    rect.SplitOX(-150f, out _, out var buttonRect);
    buttonRect = buttonRect.Shift(-ImGui.GetStyle().ItemSpacing.x, 0f);
    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.zero);
    var (nextEnabled, nextText) = stage switch
    {
      LoadStage.Configuring => (true, "Load Mods"),
      LoadStage.Loaded or LoadStage.Failed => (true, "Start Game"),
      _ => (false, "..."),
    };

    if (nextEnabled)
      ImGui.PushStyleColor(ImGuiCol.Button,
        ImGuiHelper.FlashColor(ImGuiCol.Button, ImGuiCol.ButtonActive));

    ImGui.SetCursorScreenPos(buttonRect.Min);
    ImGui.BeginDisabled(!nextEnabled);
    if (ImGui.Button(nextText, buttonRect.Size))
      next = true;
    ImGui.EndDisabled();
    if (nextEnabled)
      ImGui.PopStyleColor();
    ImGui.PopStyleVar();

    return next;
  }

  private static ChangeFlags DrawModSelectOptions(ModList modList, bool autoSort)
  {
    var changed = ChangeFlags.None;

    ImGui.AlignTextToFramePadding();

    if (ImGui.Checkbox("AutoSort", ref autoSort))
      changed |= ChangeFlags.AutoSort;

    ImGui.SameLine();
    ImGuiHelper.TextDisabled("|", true);
    ImGui.SameLine();
    ImGuiHelper.Text("Enable:");

    const byte hasEnabled = 1;
    const byte hasDisabled = 2;
    const byte hasBoth = hasEnabled | hasDisabled;

    Span<byte> states = stackalloc byte[4];
    foreach (var mod in modList.AllMods)
    {
      if (mod.Source is ModSourceType.Core)
        continue;
      var flag = mod.Enabled ? hasEnabled : hasDisabled;
      states[0] |= flag;
      states[(int)mod.Source] |= flag;
    }
    Span<byte> tgtStates = stackalloc byte[] { hasBoth, hasBoth, hasBoth, hasBoth };

    static bool SelectCheckbox(string label, byte curState, out byte nextState, bool force = false)
    {
      if (curState == 0 && !force)
      {
        nextState = hasBoth;
        return false;
      }
      ImGui.SameLine();
      ImGui.PushItemFlag(ImGuiItemFlags.MixedValue, curState == hasBoth);
      nextState = curState switch
      {
        hasDisabled => hasEnabled,
        _ => hasDisabled,
      };
      var tempState = (curState & hasEnabled) != 0;
      var res = ImGui.Checkbox(label, ref tempState);
      ImGui.PopItemFlag();
      return res;
    }

    ImGui.BeginDisabled(states[0] == 0);
    if (SelectCheckbox("All##enableAll", states[0], out var nextState, force: true))
      tgtStates.Fill(nextState);
    if (SelectCheckbox("Local##enableLocal",
        states[(int)ModSourceType.Local], out nextState))
      tgtStates[(int)ModSourceType.Local] = nextState;
    if (SelectCheckbox("Workshop##enableWorkshop",
        states[(int)ModSourceType.Workshop], out nextState))
      tgtStates[(int)ModSourceType.Workshop] = nextState;
    if (SelectCheckbox("Repo##enableRepo",
        states[(int)ModSourceType.Repo], out nextState))
      tgtStates[(int)ModSourceType.Repo] = nextState;
    ImGui.EndDisabled();

    if ((tgtStates[1] & tgtStates[2] & tgtStates[3]) != hasBoth)
    {
      foreach (var mod in modList.AllMods)
      {
        if (mod.Source is ModSourceType.Core)
          continue;
        var tgtFlags = tgtStates[(int)mod.Source];
        if (tgtFlags == hasBoth)
          continue;
        var tgt = (tgtFlags & hasEnabled) != 0;
        if (mod.Enabled != tgt)
        {
          mod.Enabled = tgt;
          changed |= ChangeFlags.Mods;
        }
      }
    }

    changed |= DrawModListToolbar(modList, loaded: false);

    ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.y);
    ImGui.Separator();

    return changed;
  }

  // Full toolbar above the mod list: list/tree toggle, sort controls, JSON export/import,
  // search box, counters and (in list view) column headers.
  private static ChangeFlags DrawModListToolbar(ModList modList, bool loaded)
  {
    var changed = DrawModListTools(modList, loaded);
    changed |= DrawPresetBar(modList, loaded);
    DrawSearchAndCount(modList, loaded);
    DrawColumnHeader(loaded);
    return changed;
  }

  // Presets: quickly switch between saved enabled/disabled + load-order configurations.
  // Distinct from Modpack import/export (which packages files for sharing/servers).
  private static ChangeFlags DrawPresetBar(ModList modList, bool loaded)
  {
    var changed = ChangeFlags.None;
    var presets = LaunchPadConfig.ListPresets();

    ImGui.AlignTextToFramePadding();
    ImGuiHelper.Text("Presets:");

    ImGui.SameLine();
    ImGui.SetNextItemWidth(170f);
    if (presets.Count == 0)
    {
      ImGui.BeginDisabled(true);
      var none = 0;
      ImGui.Combo("##presetsel", ref none, new[] { "(no presets saved)" }, 1);
      ImGui.EndDisabled();
    }
    else
    {
      presetIndex = Mathf.Clamp(presetIndex, 0, presets.Count - 1);
      var arr = presets.ToArray();
      if (ImGui.Combo("##presetsel", ref presetIndex, arr, arr.Length))
        presetNameInput = arr[presetIndex];
    }

    var hasSelection = presets.Count > 0;
    var selected = hasSelection ? presets[Mathf.Clamp(presetIndex, 0, presets.Count - 1)] : null;

    ImGui.SameLine();
    ImGui.BeginDisabled(!hasSelection || !LaunchPadConfig.CanImportModList);
    if (ImGui.Button("Load") && selected != null)
    {
      LaunchPadConfig.LoadPreset(selected);
      changed |= ChangeFlags.Mods;
    }
    ImGui.EndDisabled();
    ImGuiHelper.ItemTooltip(
      "Instantly restore this preset's enabled state and load order.\nNo files are downloaded or changed.",
      hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled);

    ImGui.SameLine();
    ImGui.BeginDisabled(!hasSelection);
    if (ImGui.Button("Delete") && selected != null)
    {
      LaunchPadConfig.DeletePreset(selected);
      presetIndex = 0;
    }
    ImGui.EndDisabled();

    ImGui.SameLine();
    ImGuiHelper.TextDisabled("|", true);
    ImGui.SameLine();
    ImGui.SetNextItemWidth(150f);
    ImGui.InputTextWithHint("##presetname", "new preset name...", ref presetNameInput, 64);

    ImGui.SameLine();
    ImGui.BeginDisabled(string.IsNullOrWhiteSpace(presetNameInput));
    if (ImGui.Button("Save"))
    {
      LaunchPadConfig.SavePreset(presetNameInput);
      var updated = LaunchPadConfig.ListPresets();
      var idx = updated.FindIndex(p => string.Equals(p, presetNameInput.Trim(), StringComparison.OrdinalIgnoreCase));
      presetIndex = Mathf.Max(0, idx);
    }
    ImGui.EndDisabled();
    ImGuiHelper.ItemTooltip("Save the current checkbox state + load order as a named preset (overwrites if the name exists).");

    return changed;
  }

  // Draws the sort selector and the JSON export/import buttons.
  // When loaded is true the load-order controls are hidden.
  private static ChangeFlags DrawModListTools(ModList modList, bool loaded = false)
  {
    var changed = ChangeFlags.None;

    {
      ImGui.AlignTextToFramePadding();
      ImGuiHelper.Text("Sort:");

      ImGui.SameLine();
      ImGui.SetNextItemWidth(140f);
      var cur = (int)sortField;
      if (ImGui.Combo("##sortfield", ref cur, SortFieldLabels, SortFieldLabels.Length))
      {
        sortField = (ModList.ModSortField)cur;
        if (!loaded && sortApplyToLoadOrder && modList.ApplySort(sortField, sortDescending))
          changed |= ChangeFlags.Mods;
      }
      ImGuiHelper.ItemTooltip("Order the mod list by load order, name, author, release date or last update.");

      ImGui.SameLine();
      if (ImGui.Button(sortDescending ? "Desc" : "Asc"))
      {
        sortDescending = !sortDescending;
        if (!loaded && sortApplyToLoadOrder && modList.ApplySort(sortField, sortDescending))
          changed |= ChangeFlags.Mods;
      }
      ImGuiHelper.ItemTooltip("Toggle ascending/descending order.");

      if (!loaded)
      {
        ImGui.SameLine();
        if (ImGui.Checkbox("Apply to load order", ref sortApplyToLoadOrder)
            && sortApplyToLoadOrder
            && modList.ApplySort(sortField, sortDescending))
          changed |= ChangeFlags.Mods;
        ImGuiHelper.ItemTooltip(
          "On: sorting also rewrites the real mod load order (drag & drop is disabled while a sort is active).\n" +
          "Off: sorting only changes how the list is displayed; the load order is left untouched.");
      }
    }

    ImGui.SameLine();
    ImGuiHelper.TextDisabled("|", true);
    ImGui.SameLine();
    ImGuiHelper.TextDisabled("Modpack:");

    ImGui.SameLine();
    ImGui.BeginDisabled(!LaunchPadConfig.CanImportModList);
    if (ImGui.Button("Import"))
      FilePicker.OpenLoad("Import Mod Package", LaunchPadPaths.SavePath, ".zip",
        path => LaunchPadConfig.ImportModPackage(path));
    ImGui.EndDisabled();
    ImGuiHelper.ItemTooltip(
      "Import a complete mod package (.zip): installs the bundled mods and applies its config.\n" +
      "For sharing full setups and dedicated servers. Use Presets to switch your own configs.",
      hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled);

    ImGui.SameLine();
    if (ImGui.Button("Export"))
      FilePicker.OpenSave("Export Mod Package", LaunchPadPaths.SavePath, "modpack.zip", ".zip",
        path => LaunchPadConfig.ExportModPackage(path));
    ImGuiHelper.ItemTooltip("Package the enabled mods (files + config) into a .zip for sharing or dedicated servers.");

    return changed;
  }

  private static string SortValueText(ModInfo mod) => sortField switch
  {
    ModList.ModSortField.Author => string.IsNullOrEmpty(mod.Author) ? "-" : mod.Author,
    ModList.ModSortField.Released => mod.Released?.ToString("yyyy-MM-dd") ?? "-",
    ModList.ModSortField.Updated => mod.Updated?.ToString("yyyy-MM-dd") ?? "-",
    _ => null,
  };

  // True when the mod matches the current search text (by name or author).
  private static bool MatchesSearch(ModInfo mod)
  {
    if (string.IsNullOrWhiteSpace(searchText))
      return true;
    var q = searchText.Trim();
    return (mod.Name?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
        || (mod.Author?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
  }

  // Search box + counters row.
  private static void DrawSearchAndCount(ModList modList, bool loaded)
  {
    ImGui.AlignTextToFramePadding();
    ImGuiHelper.Text("Search:");

    ImGui.SameLine();
    ImGui.SetNextItemWidth(200f);
    ImGui.InputTextWithHint("##modsearch", "name or author...", ref searchText, 256);

    if (!string.IsNullOrEmpty(searchText))
    {
      ImGui.SameLine();
      if (ImGui.Button("Clear##search"))
        searchText = "";
    }

    int total = 0, enabled = 0, shown = 0;
    if (loaded)
    {
      total = enabled = ModLoader.LoadedMods.Count;
      shown = ModLoader.LoadedMods.Count(m => MatchesSearch(m.Info));
    }
    else
    {
      foreach (var mod in modList.AllMods)
      {
        if (mod.Source == ModSourceType.Core)
          continue;
        total++;
        if (mod.Enabled)
          enabled++;
        if (MatchesSearch(mod))
          shown++;
      }
    }

    ImGui.SameLine();
    ImGuiHelper.TextDisabled("|", true);
    ImGui.SameLine();
    var countText = loaded ? $"Loaded: {total}" : $"Enabled: {enabled} / {total}";
    if (!string.IsNullOrWhiteSpace(searchText))
      countText += $"   (showing {shown})";
    ImGuiHelper.TextDisabled(countText);
  }

  // Fixed (non-scrolling) column headers aligned with the table columns below.
  private static void DrawColumnHeader(bool loaded)
  {
    var spacing = ImGui.GetStyle().ItemSpacing.x * 2;
    var c0 = loaded
      ? ImGui.CalcTextSize("XXX").x + spacing
      : ImGui.GetTextLineHeightWithSpacing() + spacing;
    var c1 = loaded
      ? ImGui.CalcTextSize($"{ModSourceType.Workshop}").x + spacing
      : ImGui.GetTextLineHeight() + spacing;

    var available = ImGuiHelper.AvailableRect();
    var row = available.TableRow(ImGui.GetTextLineHeightWithSpacing(), stackalloc[] { c0, c1 });

    if (loaded)
      ImGuiHelper.TextCentered(row.Column(1), "Source");

    var nameHeader = "Mod";
    if (sortField != ModList.ModSortField.LoadOrder)
      nameHeader = $"Mod   (by {SortFieldLabels[(int)sortField]} {(sortDescending ? "v" : "^")})";
    ImGuiHelper.Text(row.Column(2), nameHeader);

    if (!loaded)
      ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextRightDisabled("Author"));
  }

  // Draws the dependency tree and routes a clicked mod to the Mod Info tab.
  // Returns true when the tree changed the mod list (e.g. enabling disabled dependencies).
  private static bool DrawModTree(ModList modList)
  {
    var (changed, selected) = ModTreePanel.Draw(modList, searchText);
    if (selected != null && selected.Source != ModSourceType.Core)
    {
      selectedInfo = selected;
      selectedMod = null;
      openInfo = true;
    }
    return changed;
  }

  private static bool DrawModSelectTable(ModList modList, bool edit = false, bool autoSort = false)
  {
    var changed = false;
    if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
    {
      if (draggingMod != null && !dragged)
      {
        selectedInfo = draggingMod;
        if (selectedInfo != null && selectedInfo.Source == ModSourceType.Core)
          selectedInfo = null;
        openInfo = selectedInfo != null;
      }
      draggingMod = null;
      dragged = false;
    }

    // Display order may differ from the real load order when a sort/search is active.
    // Drag & drop reordering is only allowed when showing the full, unsorted load order.
    var displayMods = modList.Sorted(sortField, sortDescending);
    if (!string.IsNullOrWhiteSpace(searchText))
      displayMods = displayMods.Where(MatchesSearch).ToList();
    var canReorder = edit
      && sortField == ModList.ModSortField.LoadOrder
      && string.IsNullOrWhiteSpace(searchText);

    var hoveringIndex = -1;
    var draggingIndex = -1;
    if (draggingMod != null)
      draggingIndex = displayMods.IndexOf(draggingMod);

    var rowHeight = ImGui.GetTextLineHeightWithSpacing();
    var spacing = ImGui.GetStyle().ItemSpacing.x * 2;
    var available = ImGuiHelper.AvailableRect();

    var row = available.TableRow(
      rowHeight,
      stackalloc[]
      {
        rowHeight + spacing,
        ImGui.GetTextLineHeight() + spacing,
      }
    );

    var sev = BuildSeverity();

    // Independently resizable author column (persisted). Handled manually before the rows so
    // the divider takes click priority over the row selectables (which start a reorder drag).
    var maxAuthor = Mathf.Max(140f, available.Size.x - 160f);
    var authorWidth = Mathf.Clamp(LayoutPrefs.Current.AuthorWidth, 80f, maxAuthor);
    var boundaryX = available.Max.x - authorWidth;
    var splitRect = new Rect(new(boundaryX - 4f, available.Min.y), new(boundaryX + 4f, available.Max.y));
    var splitHover = ImGui.IsMouseHoveringRect(splitRect.Min, splitRect.Max);

    if (splitHover && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
      authorSplitDrag = true;
    if (authorSplitDrag)
    {
      ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
      authorWidth = Mathf.Clamp(authorWidth - ImGui.GetIO().MouseDelta.x, 80f, maxAuthor);
      LayoutPrefs.Current.AuthorWidth = authorWidth;
      boundaryX = available.Max.x - authorWidth;
      if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
      {
        authorSplitDrag = false;
        LayoutPrefs.Save();
      }
    }
    else if (splitHover)
      ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);

    ImGui.BeginDisabled(!edit);

    var idx = 0;
    foreach (var mod in displayMods)
    {
      ImGui.PushID(idx);

      if ((idx & 1) == 1)
        ImGui.GetWindowDrawList().AddRectFilled(row.Rect.TL, row.Rect.BR, RowAltColor);

      ImGui.SetCursorScreenPos(row.Column(0).TL);
      ImGui.BeginDisabled(mod.Source is ModSourceType.Core);
      if (ImGui.Checkbox("##enable", ref mod.Enabled))
        changed = true;
      ImGui.EndDisabled();

      var c12 = row.ColumnsFrom(1);
      ImGui.SetCursorScreenPos(c12.Min);
      ImGui.SetNextItemWidth(c12.Size.x);
      ImGui.Selectable($"##rowdrag", mod == draggingMod || (draggingMod == null && mod == selectedInfo));
      if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
      {
        hoveringIndex = idx;
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && draggingMod == null
            && !splitHover && !authorSplitDrag)
        {
          draggingIndex = idx;
          draggingMod = mod;
        }
      }

      // Orange accent bar on the selected row (on top of the Selectable's highlight).
      if (draggingMod == null && mod == selectedInfo)
        ImGui.GetWindowDrawList().AddRectFilled(row.Rect.TL,
          new Vector2(row.Rect.Min.x + 3f, row.Rect.Max.y),
          ImGui.ColorConvertFloat4ToU32((Vector4)LaunchPadTheme.Orange));

      DrawStatusDot(row.Column(1), StatusOf(mod, sev));

      // Name and author each get their own clipped column (no overlap).
      var nameCol = row.Column(2);
      var nameRight = Mathf.Max(nameCol.Min.x + 20f, nameCol.Max.x - authorWidth - 8f);
      var nameRect = new Rect(nameCol.Min, new Vector2(nameRight, nameCol.Max.y));
      var authorRect = new Rect(new Vector2(nameRight + 8f, nameCol.Min.y), nameCol.Max);

      var dim = !mod.Enabled && mod.Source != ModSourceType.Core;
      if (dim)
        ImGui.PushStyleColor(ImGuiCol.Text, (Vector4)ImGuiHelper.TextDisabledColor);
      ImGuiHelper.Text(nameRect, mod.Name);
      if (dim)
        ImGui.PopStyleColor();

      string rightText = null;
      if (draggingMod != null && canReorder)
      {
        if (mod.SortBefore(draggingMod))
          rightText = "Before";
        else if (draggingMod.SortBefore(mod))
          rightText = "After";
      }
      else if (!string.IsNullOrEmpty(mod.Author))
        rightText = mod.Author;

      if (rightText != null)
      {
        ImGui.PushStyleColor(ImGuiCol.Text, (Vector4)LaunchPadTheme.TextMuted);
        ImGuiHelper.Text(authorRect, rightText);
        ImGui.PopStyleColor();
      }

      ImGui.PopID();

      idx++;
      row.NextRow();
    }

    ImGui.EndDisabled();

    // Divider line between the Name and Author columns (drag handled above).
    var dividerColor = authorSplitDrag ? LaunchPadTheme.Orange
      : splitHover ? LaunchPadTheme.OrangeBorder : LaunchPadTheme.Border;
    LaunchPadTheme.HLine(new(boundaryX, available.Min.y + 2f), new(boundaryX, available.Max.y - 2f), dividerColor);

    if (canReorder && draggingIndex != -1 && hoveringIndex != -1 && draggingIndex != hoveringIndex)
    {
      dragged = true;
      if (modList.MoveModTo(draggingMod, hoveringIndex, autoSort))
        changed = true;
    }
    return changed;
  }

  private static void DrawLoadTable(ModList modList)
  {
    var style = ImGui.GetStyle();
    var available = ImGuiHelper.AvailableRect();
    var rowHei = ImGui.GetTextLineHeightWithSpacing();

    var spacing = style.ItemSpacing.x * 2;
    var row = available.TableRow(
      ImGui.GetTextLineHeightWithSpacing(),
      stackalloc[] {
      ImGui.CalcTextSize("XXX").x + spacing,
      ImGui.CalcTextSize($"{ModSourceType.Workshop}").x + spacing
    });

    // Apply the same view sorting/search as the pre-load list. The actual load order is
    // fixed at this point, so this only changes how the loaded mods are displayed.
    IEnumerable<LoadedMod> loadedMods = ModLoader.LoadedMods;
    if (sortField != ModList.ModSortField.LoadOrder)
    {
      var cmp = ModList.GetComparer(sortField, sortDescending);
      var sorted = new List<LoadedMod>(ModLoader.LoadedMods);
      sorted.Sort((a, b) => cmp.Compare(a.Info, b.Info));
      loadedMods = sorted;
    }
    if (!string.IsNullOrWhiteSpace(searchText))
      loadedMods = loadedMods.Where(m => MatchesSearch(m.Info));

    var idx = 0;
    foreach (var mod in loadedMods)
    {
      ImGui.PushID(idx);
      var info = mod.Info;
      var isSelected = selectedMod == mod;

      if ((idx & 1) == 1)
        ImGui.GetWindowDrawList().AddRectFilled(row.Rect.TL, row.Rect.BR, RowAltColor);

      ImGui.SetCursorScreenPos(row.Rect.TL);
      if (ImGui.Selectable("##scopeselect", isSelected, row.Rect.Size))
      {
        selectedInfo = isSelected ? null : info;
        selectedMod = isSelected ? null : mod;
      }

      DrawModState(row.Column(0), mod);

      ImGuiHelper.TextCentered(row.Column(1), $"{info.Source}");

      ImGuiHelper.Text(row.Column(2), info.Name);

      var sortValue = SortValueText(info);
      if (sortValue != null)
        ImGuiHelper.DrawSameLine(() => ImGuiHelper.TextRightDisabled(sortValue));

      ImGui.PopID();
      idx++;

      row.NextRow();
    }
  }

  private static void DrawModState(Rect rect, LoadedMod mod)
  {
    var (text, tooltip) = mod switch
    {
      _ when mod.Info.Source is ModSourceType.Core => ("C", "This mod contains Stationeers' assemblies and data."),
      { LoadFailed: true } => ("X", "This mod is not loaded due to an error that has occurred."),
      { LoadFinished: true } => ("+", "This mod is finished loading."),
      { LoadedEntryPoints: true } => ("...", "This mod is currently loading entrypoints."),
      { LoadedAssets: true } => ("..", "This mod is currently loading assets."),
      { LoadedAssemblies: true } => (".", "This mod is currently loading assemblies."),
      _ => ("...", "This mod is currently loading."),
    };
    ImGuiHelper.TextCentered(rect, text);
    ImGuiHelper.ItemTooltip(tooltip);
  }

  private static void DrawExportButton()
  {
    if (ImGui.Button("Export Mod Package"))
      LaunchPadConfig.ExportModPackage();
    ImGuiHelper.ItemTooltip("Package enabled mods into a zip file for dedicated servers.");
  }

  private static void DrawModInfoTab(LoadStage stage)
  {
    var open = ImGui.BeginTabItem("Mod Info", openInfo ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None);
    ImGuiHelper.ItemTooltip("View detailed mod information");
    if (open)
    {
      ImGui.BeginChild("##modinfo", ImGuiWindowFlags.HorizontalScrollbar);
      ModInfoPanel.Draw(selectedInfo);
      ImGui.EndChild();
      ImGui.EndTabItem();
    }
    else if (!openInfo && stage <= LoadStage.Loading)
      selectedInfo = null;
    openInfo = false;
  }

  private static void DrawModConfigTab(LoadStage stage)
  {
    var disabled = stage <= LoadStage.Loading;
    ImGui.BeginDisabled(disabled);
    var open = ImGui.BeginTabItem("Mod Configuration");
    ImGui.EndDisabled();
    ImGuiHelper.ItemTooltip(
      disabled ? "Mods must be loaded to edit configuration" : "Edit mod specific configuration",
      hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled
    );
    if (open)
    {
      ConfigPanel.DrawConfigEditor(selectedMod, selectedInfo);
      ImGui.EndTabItem();
    }
  }

  private static bool DrawLaunchPadConfigTab()
  {
    var changed = false;
    if (ImGui.BeginTabItem("LaunchPad Configuration"))
    {
      DrawExportButton();
      changed = ConfigPanel.DrawConfigFile(Configs.Sorted, category => category != "Internal");
      ImGui.EndTabItem();
    }
    return changed;
  }

}
