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
    NextStep = 1 << 1,
  }

  private enum Page { ModInfo, ModSettings, Profiles, Betas, ServerPackage, Settings }

  private static LoadStage lastStage;
  private static ModInfo selectedInfo = null;
  private static LoadedMod selectedMod = null;
  private static Page page = Page.ModInfo;
  private static bool openInfo = false;
  private static bool openProfiles = false;
  private static bool logExpanded = false;
  private enum ModListView { Alphabetical, Workshop, Local, LoadOrder }
  private static readonly string[] ListViewLabels = ["A-Z", "Workshop", "Local", "Load order"];
  private static ModListView listView = ModListView.Alphabetical;
  private static string modSearch = "";

  public static void OpenProfilesTab()
  {
    openProfiles = true;
    ProfilePanel.SelectActive();
  }

  public static void OpenModInfoTab()
  {
    openProfiles = false;
    openInfo = true;
  }

  public static ChangeFlags Draw(LoadStage stage, ModList modList, ProfileManager profileManager)
  {
    Platform.SetBackgroundEnabled(false);
    var changed = ChangeFlags.None;
    profileManager.Initialize();

    // when we move into the loading step, clear the selected mod so all the logs are visible
    if (lastStage < LoadStage.Loaded && stage >= LoadStage.Loading)
      selectedInfo = null;
    lastStage = stage;

    ImGuiHelper.Draw(() =>
    {
      var style = ImGui.GetStyle();
      var windowRect = ImGuiHelper.ScreenRect().Shrink(25f);
      ImGuiHelper.SetNextWindowRect(windowRect);
      ImGui.Begin("##preloadermanual", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);

      var content = ImGuiHelper.AvailableRect();
      var gap = style.ItemSpacing.x * 2f;
      var lineHeight = ImGui.GetTextLineHeightWithSpacing();
      var bottomHeight = lineHeight * 7f + ImGui.GetFrameHeightWithSpacing() + gap;
      var leftWidth = Mathf.Clamp(content.Size.x * 0.5f, 380f, 1400f);
      var actionWidth = Mathf.Clamp(content.Size.x * 0.28f, 360f, 720f);

      content.SplitOY(-bottomHeight, out var mainRect, out var bottomRect);
      mainRect = mainRect.Shrink(0f, 0f, 0f, gap);
      bottomRect.SplitOX(-actionWidth, out var galleryRect, out var actionRect);
      galleryRect = galleryRect.Shrink(0f, 0f, gap, 0f);
      mainRect.SplitOX(leftWidth, out var listRect, out var rightRect);
      rightRect = rightRect.Shrink(gap, 0f, 0f, 0f);
      var logHeight = logExpanded ? Math.Max(bottomHeight, rightRect.Size.y * 0.65f) : bottomHeight;
      rightRect.SplitOY(-logHeight, out var pageRect, out var logRect);
      pageRect = pageRect.Shrink(0f, 0f, 0f, gap);

      var dividerColor = LaunchPadTheme.OverU32(Color.white, 0.12f);
      var drawList = ImGui.GetWindowDrawList();
      drawList.AddLine(new Vector2(listRect.R + gap / 2f, mainRect.T), new Vector2(listRect.R + gap / 2f, mainRect.B), dividerColor);
      drawList.AddLine(new Vector2(content.L, bottomRect.T - gap / 2f), new Vector2(content.R, bottomRect.T - gap / 2f), dividerColor);
      drawList.AddLine(new Vector2(actionRect.L - gap / 2f, bottomRect.T), new Vector2(actionRect.L - gap / 2f, bottomRect.B), dividerColor);
      drawList.AddLine(new Vector2(rightRect.L, logRect.T - gap / 2f), new Vector2(rightRect.R, logRect.T - gap / 2f), dividerColor);

      // mod list
      ImGui.SetCursorScreenPos(listRect.Min);
      ImGui.BeginChild("##left", listRect.Size);
      if (page == Page.ServerPackage)
      {
        Widgets.SectionHeader("In the server package");
        ImGui.BeginChild("##packagelist");
        DrawPackageList(profileManager, modList);
        ImGui.EndChild();
      }
      else if (stage is LoadStage.Searching or LoadStage.Configuring)
      {
        DrawPackSummary(profileManager, modList);
        DrawModSelectOptions(modList);
        ImGui.BeginChild("##modlist");
        if (DrawModSelectTable(modList, profileManager, stage == LoadStage.Configuring))
          changed |= ChangeFlags.Mods;
        ImGui.EndChild();
      }
      else if (stage is LoadStage.Loading or LoadStage.Loaded or LoadStage.Failed)
      {
        Widgets.SectionHeader("Load order");
        ImGui.BeginChild("##loadlist");
        DrawLoadTable(modList);
        ImGui.EndChild();
      }
      ImGui.EndChild();

      // packs
      ImGui.SetCursorScreenPos(galleryRect.Min);
      ImGui.BeginChild("##packs", galleryRect.Size);
      if (DrawPackGallery(stage, profileManager, modList))
        changed |= ChangeFlags.Mods;
      ImGui.EndChild();

      // main action
      ImGui.SetCursorScreenPos(actionRect.Min);
      ImGui.BeginChild("##action", actionRect.Size);
      var action = DrawActionBox(stage, profileManager, modList);
      if (action.next)
        changed |= ChangeFlags.NextStep;
      if (action.mods)
        changed |= ChangeFlags.Mods;
      ImGui.EndChild();

      // pages
      ImGui.SetCursorScreenPos(pageRect.Min);
      ImGui.BeginChild("##right", pageRect.Size);
      changed |= DrawPages(stage, profileManager, modList);
      ImGui.EndChild();

      // log
      ImGui.SetCursorScreenPos(logRect.Min);
      ImGui.BeginChild("##logbox", logRect.Size);
      DrawLogBox();
      ImGui.EndChild();

      ImGui.End();
    });
    return changed;
  }

  private static ChangeFlags DrawPages(LoadStage stage, ProfileManager profileManager, ModList modList)
  {
    var changed = ChangeFlags.None;
    var settingsDisabled = stage <= LoadStage.Loading;
    var profilesDisabled = stage is not LoadStage.Searching and not LoadStage.Configuring;

    if (openProfiles && !profilesDisabled)
    {
      page = Page.Profiles;
      openProfiles = false;
    }
    if (openInfo)
    {
      // keep the settings page open when picking another mod there
      if (page != Page.ModSettings || settingsDisabled)
        page = Page.ModInfo;
      openInfo = false;
    }
    // show Mod Info while the chosen page is unavailable, but remember the choice
    var shown = (page == Page.ModSettings && settingsDisabled) || (page == Page.Profiles && profilesDisabled)
      ? Page.ModInfo
      : page;

    var nav = new Widgets.NavItem[]
    {
      new() { Label = "Mod Info", Tooltip = "View detailed mod information" },
      new()
      {
        Label = "Mod Settings", Disabled = settingsDisabled,
        Tooltip = settingsDisabled ? "Mods must be loaded to edit configuration" : "Edit mod specific configuration",
      },
      new()
      {
        Label = "Mod Packs", Disabled = profilesDisabled,
        Tooltip = profilesDisabled ? "Packs can only be changed before mods load" : "Your mod packs, always-on mods and share codes",
      },
      new() { Label = "Betas", Tooltip = "Switch mods between stable and beta versions" },
      new() { Label = "Server Package", Tooltip = "Export a pack's mods for a dedicated server" },
      new() { Label = "LaunchPad Settings" },
    };
    var clicked = Widgets.NavBar("##pages", nav, (int)shown);
    if (clicked >= 0)
      page = (Page)clicked;

    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(ImGui.GetStyle().ItemSpacing.x * 2f, 6f));
    ImGui.BeginChild("##page", Vector2.zero, false, ImGuiWindowFlags.AlwaysUseWindowPadding);
    ImGui.PopStyleVar();
    switch (shown)
    {
      case Page.ModInfo:
        ModInfoPanel.Draw(selectedInfo);
        break;
      case Page.ModSettings:
        if (selectedInfo != null)
          Widgets.PageHeader(selectedInfo.Name, "Settings for this mod. Some changes only apply after a restart.");
        ConfigPanel.DrawConfigEditor(selectedMod ?? ModLoader.LoadedMods.FirstOrDefault(mod => mod.Info == selectedInfo), selectedInfo);
        break;
      case Page.Profiles:
        Widgets.PageHeader("Mod Packs", "A pack is a set of mods you can switch to before loading. Changes in the mod list save to the active pack.");
        if (ProfilePanel.Draw(stage, profileManager, modList))
          changed |= ChangeFlags.Mods;
        break;
      case Page.Betas:
        if (BetaProgramsPanel.Draw(stage, modList))
        {
          // switching stable/beta flips enabled flags; carry that into the packs
          profileManager.AbsorbEnabledChanges(modList);
          changed |= ChangeFlags.Mods;
        }
        break;
      case Page.ServerPackage:
        ServerPackagePanel.Draw(profileManager, modList);
        break;
      case Page.Settings:
        // If we changed launchpad config and haven't loaded mods yet, mark mods changed to apply disable/sort behaviour
        if (DrawLaunchPadSettings(stage) && stage <= LoadStage.Configuring)
          changed |= ChangeFlags.Mods;
        break;
    }
    ImGui.EndChild();
    return changed;
  }

  private static (bool next, bool mods) DrawActionBox(LoadStage stage, ProfileManager profileManager, ModList modList)
  {
    var next = false;
    var modsChanged = false;
    var activeProfile = profileManager.ActiveProfile;
    var missing = stage == LoadStage.Configuring && activeProfile != null
      ? ProfileManager.GetMissingMods(activeProfile, modList)
      : [];
    var workshopMissing = missing.Where(entry => entry.WorkshopHandle > 1).ToList();

    var status = missing.Count > 0
      ? $"{activeProfile.Name} needs {missing.Count} mod{(missing.Count == 1 ? "" : "s")} that {(missing.Count == 1 ? "isn't" : "aren't")} installed"
      : stage switch
      {
        LoadStage.Updating => "Checking for updates to StationeersLaunchPad",
        LoadStage.Initializing => "Initializing core components",
        LoadStage.Searching => "Locating installed local and workshop mods",
        LoadStage.Configuring => "Ready to load mods",
        LoadStage.Loading => "Loading selected mods",
        LoadStage.Loaded => "Ready to start game",
        LoadStage.Failed => "Mods failed to load. Game may not function properly",
        _ => "",
      };
    var statusColor = missing.Count > 0 ? LaunchPadTheme.Warn
      : stage == LoadStage.Failed ? LaunchPadTheme.Err
      : LaunchPadTheme.Text;

    ImGuiHelper.TextColored(status, statusColor);
    ImGui.SameLine();
    ImGuiHelper.TextRightDisabled($"SLP {LaunchPadInfo.VERSION}");

    if (missing.Count > 0)
    {
      var names = string.Join(", ", missing.Take(3).Select(ProfilePanel.GetFallbackName));
      if (missing.Count > 3)
        names += $" and {missing.Count - 3} more";
      ImGui.PushTextWrapPos(0f);
      ImGuiHelper.TextColored(names, LaunchPadTheme.TextSub);
      if (workshopMissing.Count < missing.Count)
        ImGuiHelper.TextColored(profileManager.IsEditable(activeProfile)
          ? "Local mods can't be downloaded here. Install them, or remove them from the pack."
          : "Local mods can't be downloaded here. Ask the server owner, or pick another pack.",
          LaunchPadTheme.TextMuted);
      ImGui.PopTextWrapPos();
      if (profileManager.IsEditable(activeProfile))
      {
        if (ImGui.SmallButton("Remove them from the pack"))
        {
          modsChanged |= profileManager.RemoveMods(activeProfile.Name, missing);
          profileManager.Apply(modList);
        }
        ImGuiHelper.ItemTooltip($"Take the missing mods out of {activeProfile.Name}.");
      }
    }
    else
    {
      var enabledCount = modList.AllMods.Count(mod => mod.Enabled && mod.Source != ModSourceType.Core);
      var packText = activeProfile == null ? $"{enabledCount} mods enabled"
        : $"{activeProfile.Name}    {enabledCount} mod{(enabledCount == 1 ? "" : "s")} enabled";
      ImGuiHelper.TextColored(packText, LaunchPadTheme.TextMuted);
    }

    var (nextEnabled, nextText) = stage switch
    {
      LoadStage.Configuring when ProfilePanel.Busy => (false, ProfilePanel.BusyText),
      LoadStage.Configuring when BetaProgramsPanel.Busy => (false, "Updating Betas..."),
      LoadStage.Configuring when workshopMissing.Count > 0 =>
        (true, $"Download {workshopMissing.Count} mod{(workshopMissing.Count == 1 ? "" : "s")}"),
      LoadStage.Configuring when missing.Count > 0 => (false, "Load Mods"),
      LoadStage.Configuring => (true, "Load Mods"),
      LoadStage.Loading => (false, "Loading Mods..."),
      LoadStage.Loaded or LoadStage.Failed => (true, "Start Game"),
      _ => (false, "Please wait..."),
    };

    var avail = ImGui.GetContentRegionAvail();
    var buttonHeight = Math.Min(ImGui.GetFrameHeight() * 2.4f, avail.y - ImGui.GetStyle().ItemSpacing.y);
    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + avail.y - buttonHeight - ImGui.GetStyle().ItemSpacing.y);
    avail = new Vector2(avail.x, buttonHeight);
    if (stage == LoadStage.Loading)
    {
      var mods = ModLoader.LoadedMods;
      var done = mods.Count(mod => mod.LoadFinished || mod.LoadFailed);
      var fraction = mods.Count == 0 ? 0f : (float)done / mods.Count;
      ImGui.PushStyleColor(ImGuiCol.PlotHistogram, (Vector4)LaunchPadTheme.Accent);
      ImGui.PushStyleColor(ImGuiCol.FrameBg, (Vector4)LaunchPadTheme.Over(Color.white, 0.06f));
      ImGui.ProgressBar(fraction, new Vector2(avail.x, buttonHeight), $"Loading mods  {done} / {mods.Count}");
      ImGui.PopStyleColor(2);
      return (false, modsChanged);
    }

    if (Widgets.PrimaryButton($"{nextText}##next", new Vector2(avail.x, buttonHeight), nextEnabled))
    {
      if (workshopMissing.Count > 0)
        ProfilePanel.Download(workshopMissing);
      else
        next = true;
    }
    if (workshopMissing.Count > 0)
      ImGuiHelper.ItemTooltip("Subscribe to the missing Workshop mods. Loading continues once everything is installed.");
    else if (nextEnabled)
      ImGuiHelper.ItemTooltip("Space also continues.");
    return (next, modsChanged);
  }

  private static void DrawLogBox()
  {
    var logger = selectedMod?.Logger ?? Logger.Global;
    var title = selectedMod == null ? "LOG" : $"LOG    {selectedMod.Info.Name.ToUpperInvariant()}";
    ImGui.AlignTextToFramePadding();
    ImGuiHelper.TextColored(title, LaunchPadTheme.TextMuted);
    ImGui.SameLine();
    ImGui.SetNextItemWidth(ImGui.CalcTextSize("Information").x + ImGui.GetFrameHeight() * 2f);
    ConfigPanel.DrawEnumEntry(Configs.LogSeverities, Configs.LogSeveritiesWrapper, false);

    var expandText = logExpanded ? "Collapse" : "Expand";
    var popText = "Open window";
    var spacing = ImGui.GetStyle().ItemSpacing.x;
    var buttonsWidth = ImGui.CalcTextSize(expandText).x + ImGui.CalcTextSize(popText).x
      + ImGui.GetStyle().FramePadding.x * 4f + spacing;
    ImGui.SameLine(ImGui.GetWindowContentRegionMax().x - buttonsWidth);
    if (ImGui.Button(expandText))
      logExpanded = !logExpanded;
    ImGui.SameLine();
    if (ImGui.Button(popText))
      LogPanel.OpenStandaloneLogs();

    var consoleHeight = ImGui.GetFrameHeightWithSpacing();
    var linesSize = ImGui.GetContentRegionAvail() - new Vector2(0f, consoleHeight);
    ImGui.PushStyleColor(ImGuiCol.ChildBg, (Vector4)LaunchPadTheme.Over(Color.black, 0.35f));
    ImGui.BeginChild("##loglines", linesSize);
    ImGui.PopStyleColor();
    LogPanel.DrawLines(logger, wrap: true);
    ImGui.EndChild();

    var consoleRect = ImGuiHelper.AvailableRect();
    StartupConsole.DrawInput(consoleRect);
  }

  private static void DrawModSelectOptions(ModList modList)
  {
    ImGui.SetNextItemWidth(-float.Epsilon);
    ImGui.InputTextWithHint("##modsearch", "Search mods...", ref modSearch, 256);
    listView = (ModListView)Widgets.Segmented("##listview", ListViewLabels, (int)listView);
    ImGuiHelper.ItemTooltip("Local includes repository mods.");
    var total = modList.AllMods.Count(mod => mod.Source != ModSourceType.Core);
    var enabled = modList.AllMods.Count(mod => mod.Enabled && mod.Source != ModSourceType.Core);
    ImGui.SameLine();
    ImGui.AlignTextToFramePadding();
    ImGuiHelper.TextRightDisabled($"{enabled} of {total} enabled");
    ImGui.Spacing();
  }

  private static bool MatchesListFilter(ModInfo mod) =>
    (listView != ModListView.Workshop || mod.Source == ModSourceType.Workshop)
    && (listView != ModListView.Local || mod.Source is ModSourceType.Local or ModSourceType.Repo)
    && (string.IsNullOrWhiteSpace(modSearch)
      || (mod.Name ?? "").IndexOf(modSearch.Trim(), StringComparison.OrdinalIgnoreCase) >= 0
      || (mod.ModID ?? "").IndexOf(modSearch.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);

  // two text lines next to a 16:9 preview
  private static (float rowHeight, float padding, float thumbHeight, float thumbWidth) RowMetrics()
  {
    var lineHeight = ImGui.GetTextLineHeight();
    var padding = Mathf.Round(lineHeight * 0.3f);
    var rowHeight = lineHeight * 2f + ImGui.GetStyle().ItemSpacing.y + padding * 2f;
    var thumbHeight = rowHeight - padding * 2f;
    return (rowHeight, padding, thumbHeight, Mathf.Round(thumbHeight * 16f / 9f));
  }

  private static void DrawPackSummary(ProfileManager profileManager, ModList modList)
  {
    var active = profileManager.ActiveProfile;
    if (active == null)
      return;
    var count = ProfileManager.ModCount(active);
    var alwaysOn = profileManager.AlwaysOnActive ? ProfileManager.ModCount(profileManager.AlwaysOn) : 0;
    ImGuiHelper.TextColored(active.Name, LaunchPadTheme.Accent);
    ImGui.SameLine();
    var summary = profileManager.IsVanilla(active) ? "no mods, not even always-on ones"
      : profileManager.IsBuiltIn(active) ? $"only your {alwaysOn} always-on mod{(alwaysOn == 1 ? "" : "s")}"
      : $"{count} mod{(count == 1 ? "" : "s")}{(alwaysOn > 0 ? $", plus {alwaysOn} always on" : "")}";
    ImGuiHelper.TextColored(summary, LaunchPadTheme.TextMuted);
    if (ProfileManager.IsServerPack(active))
    {
      ImGui.PushTextWrapPos(0f);
      ImGuiHelper.TextColored(
        $"Kept in sync with {active.ServerName}, so its mods can't be changed here. Your always-on mods load on top.",
        LaunchPadTheme.Info);
      ImGui.PopTextWrapPos();
    }
    else if (profileManager.IsBuiltIn(active))
    {
      ImGui.PushTextWrapPos(0f);
      ImGuiHelper.TextColored("Built-in pack. Pick or create a pack below to choose mods.", LaunchPadTheme.TextSub);
      ImGui.PopTextWrapPos();
    }
    ImGui.Spacing();
  }

  private static bool DrawPackGallery(LoadStage stage, ProfileManager profileManager, ModList modList)
  {
    if (profileManager.ActiveProfile == null)
      return false;
    var changed = false;
    ImGuiHelper.TextColored("PACKS", LaunchPadTheme.TextMuted);
    var size = ImGui.GetContentRegionAvail();
    var picked = PackGallery.Draw("##gallery", profileManager, modList, size, true, out var newClicked,
      enabled: stage == LoadStage.Configuring && !ProfilePanel.Busy);
    if (picked != null)
      changed |= profileManager.ApplyProfile(picked.Name, modList);
    if (newClicked)
    {
      newPackName = "";
      ImGui.OpenPopup("##newpack");
    }
    changed |= DrawNewPackPopup(profileManager, modList);
    return changed;
  }

  private static string newPackName = "";

  // enter creates an empty pack
  private static bool DrawNewPackPopup(ProfileManager profileManager, ModList modList)
  {
    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f));
    var open = ImGui.BeginPopup("##newpack");
    ImGui.PopStyleVar();
    if (!open)
      return false;

    var changed = false;
    var style = ImGui.GetStyle();
    var current = profileManager.ActiveProfile is { } active && !profileManager.IsBuiltIn(active) ? active : null;
    var copyText = current == null ? "" : $"Copy {current.Name}";
    var emptyText = "Empty";
    var buttonWidth = Math.Max(110f, Math.Max(ImGui.CalcTextSize(copyText).x, ImGui.CalcTextSize(emptyText).x)
      + style.FramePadding.x * 4f);
    var width = current == null ? Math.Max(460f, buttonWidth) : Math.Max(460f, buttonWidth * 2f + style.ItemSpacing.x);
    if (current != null)
      buttonWidth = (width - style.ItemSpacing.x) / 2f;
    else
      buttonWidth = width;

    ImGuiHelper.TextColored("New pack", LaunchPadTheme.TextMuted);
    if (ImGui.IsWindowAppearing())
      ImGui.SetKeyboardFocusHere();
    ImGui.SetNextItemWidth(width);
    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(style.FramePadding.x * 2f, style.FramePadding.y * 3f));
    var enter = ImGui.InputTextWithHint("##newpackname", "Change me!", ref newPackName, 80,
      ImGuiInputTextFlags.EnterReturnsTrue);
    ImGui.PopStyleVar();
    var trimmed = newPackName.Trim();
    var valid = ProfileStorage.IsValidName(trimmed) && !ProfileManager.IsReservedName(trimmed)
      && profileManager.FindProfile(trimmed) == null;
    if (!valid && trimmed.Length > 0)
      ImGuiHelper.TextColored(profileManager.FindProfile(trimmed) != null ? "Name already taken" : "No special characters",
        LaunchPadTheme.Warn);

    ImGui.BeginDisabled(!valid);
    var empty = ImGui.Button(emptyText, new Vector2(buttonWidth, ImGui.GetFrameHeight() * 1.4f)) || (enter && valid);
    ImGuiHelper.ItemTooltip("Start with no mods. Always-on mods still load.");
    var copy = false;
    if (current != null)
    {
      ImGui.SameLine();
      copy = ImGui.Button(copyText, new Vector2(buttonWidth, ImGui.GetFrameHeight() * 1.4f));
      ImGuiHelper.ItemTooltip($"Start with the mods of {current.Name}.");
    }
    ImGui.EndDisabled();

    if ((empty || copy) && valid && profileManager.CreatePack(trimmed, copy ? current : null))
    {
      changed = profileManager.ApplyProfile(trimmed, modList);
      ImGui.CloseCurrentPopup();
    }
    ImGui.EndPopup();
    return changed;
  }

  private static bool DrawModSelectTable(ModList modList, ProfileManager profileManager, bool edit = false)
  {
    var changed = false;

    var active = profileManager.ActiveProfile;
    var packsOn = active != null;
    var lineHeight = ImGui.GetTextLineHeight();
    var (rowHeight, rowPadding, thumbHeight, thumbWidth) = RowMetrics();
    var spacing = ImGui.GetStyle().ItemSpacing.x * 2;
    var checkboxSize = ImGui.GetFrameHeight();
    var toggleWidth = packsOn ? checkboxSize + spacing : 0f;
    var available = ImGuiHelper.AvailableRect();

    var row = available.TableRow(
      rowHeight,
      stackalloc[]
      {
        checkboxSize + spacing,
        thumbWidth + spacing,
      }
    );
    var drawList = ImGui.GetWindowDrawList();

    // unchecked checkboxes are invisible on the dark background otherwise
    ImGui.PushStyleColor(ImGuiCol.FrameBg, (Vector4)LaunchPadTheme.Over(Color.white, 0.06f));
    ImGui.PushStyleColor(ImGuiCol.Border, (Vector4)LaunchPadTheme.Over(Color.white, 0.28f));
    ImGui.PushStyleColor(ImGuiCol.Header, (Vector4)LaunchPadTheme.Over(LaunchPadTheme.Accent, 0.2f));
    ImGui.PushStyleColor(ImGuiCol.HeaderHovered, (Vector4)LaunchPadTheme.Over(Color.white, 0.07f));
    ImGui.PushStyleColor(ImGuiCol.HeaderActive, (Vector4)LaunchPadTheme.Over(LaunchPadTheme.Accent, 0.3f));
    ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
    var loadPosition = 0;

    ImGui.BeginDisabled(!edit);

    var idx = 0;
    IEnumerable<ModInfo> visibleMods = modList.AllMods;
    if (listView != ModListView.LoadOrder)
      visibleMods = visibleMods.OrderBy(mod => mod.Source != ModSourceType.Core)
        .ThenBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(mod => mod.Source.ToString(), StringComparer.Ordinal)
        .ThenBy(mod => mod.DirectoryPath, StringComparer.OrdinalIgnoreCase)
        .ThenBy(mod => mod.WorkshopHandle);
    foreach (var mod in visibleMods)
    {
      loadPosition++;
      var matchesFilter = mod.Source == ModSourceType.Core || MatchesListFilter(mod);
      if (!matchesFilter && !mod.Enabled)
        continue;
      ImGui.PushID(idx);
      ImGui.BeginDisabled(!matchesFilter);
      var isBeta = modList.IsBetaMod(mod);
      var isCore = mod.Source is ModSourceType.Core;
      var isAlwaysOn = packsOn && profileManager.IsAlwaysOn(mod);

      var rowRect = row.Rect;
      var isSelected = mod == selectedInfo;
      if (idx % 2 == 1)
        drawList.AddRectFilled(rowRect.Min, rowRect.Max,
          LaunchPadTheme.OverU32(Color.white, 0.025f));

      // the selectable stops before the always-on toggle so it stays clickable
      var content = row.ColumnsFrom(1);
      var toggleX = rowRect.Max.x - toggleWidth;
      var selectSize = new Vector2(Math.Max(1f, toggleX - content.Min.x), content.Size.y);
      ImGui.SetCursorScreenPos(content.Min);
      // Selectable grows its hit box by half the item spacing, which overlaps the next row
      ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.x, 0f));
      if (ImGui.Selectable("##rowselect", isSelected, ImGuiSelectableFlags.None, selectSize))
      {
        selectedInfo = isCore ? null : mod;
        openInfo = selectedInfo != null;
      }
      ImGui.PopStyleVar();
      if (isBeta)
        ImGuiHelper.ItemTooltip("This item is a beta version of an installed mod.");
      if (isSelected)
        drawList.AddRectFilled(rowRect.Min, new Vector2(rowRect.Min.x + 2f, rowRect.Max.y),
          ImGui.ColorConvertFloat4ToU32((Vector4)LaunchPadTheme.Accent));

      var checkboxCol = row.Column(0);
      ImGui.SetCursorScreenPos(new Vector2(
        checkboxCol.Min.x + (checkboxCol.Size.x - checkboxSize) / 2f,
        checkboxCol.Min.y + (rowHeight - checkboxSize) / 2f));
      var canToggle = !isCore && (!packsOn || (!isAlwaysOn && profileManager.ActiveEditable));
      ImGui.BeginDisabled(!canToggle);
      var enabled = mod.Enabled;
      if (ImGui.Checkbox("##enable", ref enabled))
        changed |= packsOn
          ? profileManager.SetInPack(mod, enabled, modList)
          : BetaProgramsPanel.SetModEnabled(modList, mod, enabled);
      ImGui.EndDisabled();
      if (packsOn && !isCore)
        ImGuiHelper.ItemTooltip(
          isAlwaysOn ? profileManager.AlwaysOnActive
              ? "Always on, so it loads in every pack. Turn always on off to change it here."
              : "Always on, but Vanilla loads no mods at all."
          : profileManager.IsBuiltIn(active) ? $"{active.Name} is built in. Pick or create a pack to choose mods."
          : !profileManager.ActiveEditable ? $"{active.Name} follows its server."
          : enabled ? $"In {active.Name}. Untick to remove it." : $"Tick to add it to {active.Name}.",
          hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled);

      DrawThumb(drawList, mod, row.Column(1), rowPadding, thumbWidth, thumbHeight,
        dim: !mod.Enabled && !isCore);

      // name and details, clipped before the always-on toggle
      var textCol = row.Column(2);
      ImGui.PushClipRect(textCol.Min, new Vector2(toggleX - ImGui.GetStyle().ItemSpacing.x, textCol.Max.y), true);
      var nameColor = mod.Enabled || isCore ? LaunchPadTheme.Text : LaunchPadTheme.TextSub;
      ImGui.SetCursorScreenPos(new Vector2(textCol.Min.x, textCol.Min.y + rowPadding));
      ImGuiHelper.TextColored(mod.Name ?? "", nameColor);
      if (isBeta)
      {
        ImGui.SameLine();
        ImGuiHelper.TextColored("BETA", LaunchPadTheme.Warn);
      }
      if (isAlwaysOn)
      {
        ImGui.SameLine();
        ImGuiHelper.TextColored("ALWAYS ON", LaunchPadTheme.Accent);
      }
      ImGui.SetCursorScreenPos(new Vector2(
        textCol.Min.x, textCol.Min.y + rowPadding + lineHeight + ImGui.GetStyle().ItemSpacing.y));
      ImGuiHelper.TextColored(ModMetaLine(mod, listView == ModListView.LoadOrder ? loadPosition : 0),
        LaunchPadTheme.TextMuted);
      ImGui.PopClipRect();

      if (packsOn && !isCore)
      {
        ImGui.SetCursorScreenPos(new Vector2(toggleX + (toggleWidth - checkboxSize) / 2f,
          rowRect.Min.y + (rowHeight - checkboxSize) / 2f));
        if (DrawAlwaysOnButton(isAlwaysOn, new Vector2(checkboxSize, checkboxSize)))
        {
          if (isAlwaysOn)
            changed |= profileManager.SetAlwaysOn(mod, false, modList);
          else
          {
            confirmAlwaysOn = mod;
            openConfirm = true;
          }
        }
        ImGuiHelper.ItemTooltip(isAlwaysOn
          ? "Always on: loads in every pack except Vanilla, server packs included. Click to turn it off."
          : "Make it always on: it loads in every pack except Vanilla, server packs included. Only for mods that work purely client-side.",
          400f);
      }

      ImGui.EndDisabled();
      ImGui.PopID();

      idx++;
      row.NextRow();
    }

    ImGui.EndDisabled();
    ImGui.PopStyleVar();
    ImGui.PopStyleColor(5);

    if (confirmAlwaysOn != null)
    {
      if (openConfirm)
        ImGui.OpenPopup("##alwayson");
      openConfirm = false;
      if (DrawAlwaysOnConfirm(profileManager, modList))
        changed = true;
    }
    return changed;
  }

  private static ModInfo confirmAlwaysOn;
  private static bool openConfirm;

  // we can't tell if a mod is client-side, so the player has to confirm it
  private static bool DrawAlwaysOnConfirm(ProfileManager profileManager, ModList modList)
  {
    var changed = false;
    var display = ImGui.GetIO().DisplaySize;
    ImGui.SetNextWindowPos(display / 2f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
    ImGui.SetNextWindowSize(new Vector2(Math.Min(640f, display.x - 40f), 0f));
    ImGui.PushStyleColor(ImGuiCol.Border, (Vector4)LaunchPadTheme.Over(LaunchPadTheme.Warn, 0.6f));
    ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
    var open = ImGui.BeginPopup("##alwayson");
    ImGui.PopStyleVar();
    ImGui.PopStyleColor();
    if (!open)
    {
      confirmAlwaysOn = null;
      return false;
    }
    var mod = confirmAlwaysOn;
    ImGuiHelper.TextColored($"Make {mod.Name} always on?", LaunchPadTheme.Text);
    ImGui.Spacing();
    ImGui.PushTextWrapPos(0f);
    ImGuiHelper.TextColored(
      "It will load in every pack except Vanilla, including server packs and on servers that don't have it.",
      LaunchPadTheme.TextSub);
    ImGui.Spacing();
    ImGuiHelper.TextColored(
      "Only do this for mods that work purely on your side, like UI or quality of life mods. A mod that needs the server to have it too can break the game, desync or get you kicked when you join a server without it. LaunchPad can't tell which kind a mod is, so check its description first.",
      LaunchPadTheme.Warn);
    ImGui.PopTextWrapPos();
    ImGui.Spacing();
    if (ImGui.Button("It's client-side, make it always on"))
    {
      changed = profileManager.SetAlwaysOn(mod, true, modList);
      confirmAlwaysOn = null;
      ImGui.CloseCurrentPopup();
    }
    ImGui.SameLine();
    if (ImGui.Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape))
    {
      confirmAlwaysOn = null;
      ImGui.CloseCurrentPopup();
    }
    ImGui.EndPopup();
    return changed;
  }

  // power symbol, faint until hovered or on
  private static bool DrawAlwaysOnButton(bool on, Vector2 size)
  {
    var min = ImGui.GetCursorScreenPos();
    var clicked = ImGui.InvisibleButton("##alwayson", size);
    var hovered = ImGui.IsItemHovered();
    var color = on ? LaunchPadTheme.Accent
      : hovered ? LaunchPadTheme.TextSub
      : LaunchPadTheme.Over(Color.white, 0.22f);
    var u32 = ImGui.ColorConvertFloat4ToU32((Vector4)color);
    var drawList = ImGui.GetWindowDrawList();
    var center = min + size / 2f + new Vector2(0f, size.y * 0.04f);
    var radius = size.y * 0.3f;
    var thickness = Math.Max(1.5f, size.y * 0.08f);
    if (on)
      drawList.AddCircleFilled(center, radius * 1.45f, LaunchPadTheme.OverU32(LaunchPadTheme.Accent, 0.18f), 20);
    // the ring leaves a gap at the top for the bar
    drawList.PathArcTo(center, radius, -Mathf.PI / 2f + 0.75f, Mathf.PI * 1.5f - 0.75f, 20);
    drawList.PathStroke(u32, ImDrawFlags.None, thickness);
    drawList.AddLine(center - new Vector2(0f, radius * 1.25f), center - new Vector2(0f, radius * 0.2f), u32, thickness);
    return clicked;
  }

  private static void DrawThumb(ImDrawListPtr drawList, ModInfo mod, Rect column, float padding,
    float width, float height, bool dim)
  {
    var min = new Vector2(column.Min.x, column.Min.y + padding);
    var max = min + new Vector2(width, height);
    ModImages.DrawFill(drawList, mod, min, max);
    if (dim)
      drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.3f)));
  }

  private static string ModMetaLine(ModInfo mod, int loadPosition)
  {
    if (mod.Source == ModSourceType.Core)
      return "Stationeers";
    var parts = new List<string>();
    if (loadPosition > 0)
      parts.Add($"#{loadPosition}");
    parts.Add(mod.Source.ToString());
    var version = mod.About?.Version?.Trim();
    if (!string.IsNullOrEmpty(version))
      parts.Add($"v{version.TrimStart('v', 'V')}");
    var author = mod.About?.Author?.Trim();
    if (!string.IsNullOrEmpty(author))
      parts.Add(author);
    return string.Join(ModInfoPanel.MetaSeparator, parts);
  }

  private static void DrawPackageList(ProfileManager profileManager, ModList modList)
  {
    var mods = profileManager.ServerPackageMods(modList)
      .Where(mod => mod.Source != ModSourceType.Core)
      .OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
      .ToList();
    if (mods.Count == 0)
    {
      ImGuiHelper.TextColored("Nothing to export from this pack.", LaunchPadTheme.TextMuted);
      return;
    }

    var lineHeight = ImGui.GetTextLineHeight();
    var (rowHeight, rowPadding, thumbHeight, thumbWidth) = RowMetrics();
    var spacing = ImGui.GetStyle().ItemSpacing.x * 2;
    var available = ImGuiHelper.AvailableRect();
    var row = available.TableRow(rowHeight, stackalloc[] { thumbWidth + spacing });
    var drawList = ImGui.GetWindowDrawList();

    var idx = 0;
    foreach (var mod in mods)
    {
      var rowRect = row.Rect;
      if (idx % 2 == 1)
        drawList.AddRectFilled(rowRect.Min, rowRect.Max, LaunchPadTheme.OverU32(Color.white, 0.025f));

      DrawThumb(drawList, mod, row.Column(0), rowPadding, thumbWidth, thumbHeight, dim: false);
      var textCol = row.Column(1);
      ImGui.SetCursorScreenPos(new Vector2(textCol.Min.x, textCol.Min.y + rowPadding));
      ImGuiHelper.Text(mod.Name ?? "");
      ImGui.SetCursorScreenPos(new Vector2(
        textCol.Min.x, textCol.Min.y + rowPadding + lineHeight + ImGui.GetStyle().ItemSpacing.y));
      ImGuiHelper.TextColored(ModMetaLine(mod, 0), LaunchPadTheme.TextMuted);

      idx++;
      row.NextRow();
    }
  }

  private static void DrawLoadTable(ModList modList)
  {
    var lineHeight = ImGui.GetTextLineHeight();
    var (rowHeight, rowPadding, thumbHeight, thumbWidth) = RowMetrics();
    var spacing = ImGui.GetStyle().ItemSpacing.x * 2;
    var dotColumn = ImGui.GetFrameHeight();
    var available = ImGuiHelper.AvailableRect();
    var row = available.TableRow(rowHeight, stackalloc[] { dotColumn + spacing, thumbWidth + spacing });
    var drawList = ImGui.GetWindowDrawList();

    ImGui.PushStyleColor(ImGuiCol.Header, (Vector4)LaunchPadTheme.Over(LaunchPadTheme.Accent, 0.2f));
    ImGui.PushStyleColor(ImGuiCol.HeaderHovered, (Vector4)LaunchPadTheme.Over(Color.white, 0.07f));
    ImGui.PushStyleColor(ImGuiCol.HeaderActive, (Vector4)LaunchPadTheme.Over(LaunchPadTheme.Accent, 0.3f));

    var idx = 0;
    foreach (var mod in ModLoader.LoadedMods)
    {
      ImGui.PushID(idx);
      var info = mod.Info;
      var isSelected = selectedMod == mod;
      var rowRect = row.Rect;
      if (idx % 2 == 1)
        drawList.AddRectFilled(rowRect.Min, rowRect.Max, LaunchPadTheme.OverU32(Color.white, 0.025f));

      ImGui.SetCursorScreenPos(rowRect.TL);
      ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.x, 0f));
      if (ImGui.Selectable("##scopeselect", isSelected, ImGuiSelectableFlags.None, rowRect.Size))
      {
        selectedInfo = isSelected ? null : info;
        selectedMod = isSelected ? null : mod;
      }
      ImGui.PopStyleVar();
      var (state, stateColor, tooltip) = ModState(mod);
      ImGuiHelper.ItemTooltip(tooltip);
      if (isSelected)
        drawList.AddRectFilled(rowRect.Min, new Vector2(rowRect.Min.x + 2f, rowRect.Max.y),
          ImGui.ColorConvertFloat4ToU32((Vector4)LaunchPadTheme.Accent));

      var dotCol = row.Column(0);
      drawList.AddCircleFilled(dotCol.Min + dotCol.Size / 2f, lineHeight * 0.22f,
        ImGui.ColorConvertFloat4ToU32((Vector4)stateColor));

      DrawThumb(drawList, info, row.Column(1), rowPadding, thumbWidth, thumbHeight, dim: false);

      var textCol = row.Column(2);
      ImGui.SetCursorScreenPos(new Vector2(textCol.Min.x, textCol.Min.y + rowPadding));
      ImGuiHelper.Text(info.Name ?? "");
      if (modList.IsBetaMod(info))
      {
        ImGui.SameLine();
        ImGuiHelper.TextColored("BETA", LaunchPadTheme.Warn);
      }
      ImGui.SetCursorScreenPos(new Vector2(
        textCol.Min.x, textCol.Min.y + rowPadding + lineHeight + ImGui.GetStyle().ItemSpacing.y));
      ImGuiHelper.TextColored(state, stateColor);
      ImGui.SameLine();
      ImGuiHelper.TextColored(ModMetaLine(info, 0), LaunchPadTheme.TextMuted);

      ImGui.PopID();
      idx++;
      row.NextRow();
    }

    ImGui.PopStyleColor(3);
  }

  private static (string, Color, string) ModState(LoadedMod mod) => mod switch
  {
    _ when mod.Info.Source is ModSourceType.Core =>
      ("Core", LaunchPadTheme.TextMuted, "This mod contains Stationeers' assemblies and data."),
    { LoadFailed: true } => ("Failed", LaunchPadTheme.Err, "This mod is not loaded due to an error that has occurred."),
    { LoadFinished: true } => ("Loaded", LaunchPadTheme.Ok, "This mod is finished loading."),
    { LoadedEntryPoints: true } => ("Entrypoints", LaunchPadTheme.Warn, "This mod is currently loading entrypoints."),
    { LoadedAssets: true } => ("Assets", LaunchPadTheme.Warn, "This mod is currently loading assets."),
    { LoadedAssemblies: true } => ("Assemblies", LaunchPadTheme.Warn, "This mod is currently loading assemblies."),
    _ => ("Waiting", LaunchPadTheme.TextMuted, "This mod is currently loading."),
  };

  private static bool DrawLaunchPadSettings(LoadStage stage)
  {
    Widgets.PageHeader("LaunchPad Settings", "Options for StationeersLaunchPad itself.");

    Widgets.SectionHeader("Tools");
    var canReload = stage == LoadStage.Configuring && !ProfilePanel.Busy
      && !BetaProgramsPanel.Busy;
    ImGui.BeginDisabled(!canReload);
    if (ImGui.Button("Reload mod list"))
      LaunchPadConfig.ReloadMods();
    ImGui.EndDisabled();
    ImGui.SameLine();
    ImGuiHelper.TextColored(canReload
      ? "Re-scan local, Workshop, and repository mod files from disk."
      : "The mod list can only be reloaded while configuring mods.", LaunchPadTheme.TextMuted);

    ImGui.Spacing();
    return ConfigPanel.DrawConfigFile(Configs.Sorted, category => category != "Internal");
  }
}
