using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using ImGuiNET;
using StationeersLaunchPad.UI;
using UnityEngine;

namespace StationeersLaunchPad.News;

public static class NewsPopup
{
  private static bool visible;
  private static List<NewsEntry> activeEntries = [];
  private static int detailIndex = -1;
  private static bool showConfirm;
  private static int confirmIndex = -1;
  private static DateTime? infoTimerStart;
  private static string actionStatus;
  private static bool isActionBusy;

  private static bool actionCompleted;
  private static bool actionSucceeded;
  private static string actionResultMessage;

  public static bool IsVisible => visible;

  public static async UniTask Run(List<NewsEntry> entries)
  {
    if (Platform.IsServer)
      return;
    if (entries == null || entries.Count == 0)
      return;

    activeEntries = [.. entries];
    detailIndex = -1;
    showConfirm = false;
    confirmIndex = -1;
    infoTimerStart = null;
    actionStatus = null;
    isActionBusy = false;
    actionCompleted = false;
    actionSucceeded = false;
    actionResultMessage = null;

    visible = true;
    while (visible)
      await UniTask.Yield();

    activeEntries.Clear();
  }

  public static void Draw()
  {
    if (!visible)
      return;
    ImGuiHelper.Draw(DrawInternal);
  }

  private static void DrawInternal()
  {
    if (!visible || activeEntries.Count == 0)
    {
      visible = false;
      return;
    }

    // For pure-info notices (1 or many), start the auto-ack timer immediately
    // so they don't block (forever). Show detail view (with countdown) instead of list.
    if (detailIndex < 0 && !showConfirm && activeEntries.All(e => e.Type == "info"))
    {
      detailIndex = 0;
      StartInfoTimerIfInfo(activeEntries[0]);
    }

    bool useList = activeEntries.Count > 1 && detailIndex < 0 && !showConfirm;

    var screen = ImGuiHelper.ScreenRect();
    var w = screen.Size.x * 0.58f;
    var maxH = screen.Size.y * 0.9f;

    var size = new Vector2(w, 0);  // height 0 = size to content 
    ImGui.SetNextWindowSize(size);
    ImGui.SetNextWindowPos(new Vector2((screen.Size.x - w) * 0.5f, screen.Size.y * 0.1f));
    ImGui.SetNextWindowFocus();

    ImGui.SetNextWindowSizeConstraints(
        new Vector2(500, 500),
        new Vector2(w, maxH));

    var flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings;
    if (!ImGui.Begin("LaunchPad Notices##SLPNews", flags))
    {
      ImGui.End();
      return;
    }

    if (useList)
      DrawListView();
    else if (showConfirm)
      DrawConfirmView();
    else
      DrawDetailView();

    CheckInfoAutoAdvance();

    ImGui.End();
  }

  private static void CheckInfoAutoAdvance()
  {
    if (infoTimerStart == null || detailIndex < 0 || detailIndex >= activeEntries.Count)
      return;
    var entry = activeEntries[detailIndex];
    if (entry.Type != "info")
      return;
    if ((DateTime.UtcNow - infoTimerStart.Value).TotalSeconds >= 10.0)
    {
      HandleHandled(detailIndex, persist: false);
    }
  }

  private static void DrawListView()
  {
    ImGuiHelper.Text("Something requires your attention. Click a notice to view details.");
    ImGui.Separator();

    ImGui.Dummy(new Vector2(0, 15)); // top breathing
    ImGui.BeginChild("##newslist", new Vector2(0, -ImGui.GetFrameHeightWithSpacing() * 4));

    for (int i = 0; i < activeEntries.Count; i++)
    {
      var e = activeEntries[i];
      var color = GetSeverityColor(e.Severity);
      var shortDesc = System.Text.RegularExpressions.Regex.Replace(e.ShortDescription ?? "", @"\[[^\]]*\]", "");
      var label = $"{SeverityBadge(e.Severity)} {e.Heading} - {shortDesc}";
      ImGui.PushStyleColor(ImGuiCol.Text, (Vector4)color);
      if (ImGui.Selectable($"{label}##n{i}"))
      {
        detailIndex = i;
        StartInfoTimerIfInfo(e);
        actionCompleted = false;
        actionSucceeded = false;
        actionResultMessage = null;
        actionStatus = null;
        isActionBusy = false;
      }
      ImGui.PopStyleColor();
      if (ImGui.IsItemHovered())
        ImGuiHelper.TextTooltip("Click to view details");
    }

    ImGui.EndChild();
    ImGui.Separator();
    ImGuiHelper.TextDisabled("You must view and handle all notices before the game can continue loading. Click one to get started.");
  }

  private static void DrawDetailView()
  {
    if (detailIndex < 0 || detailIndex >= activeEntries.Count)
      detailIndex = 0;

    var entry = activeEntries[detailIndex];

    var color = GetSeverityColor(entry.Severity);
    ImGuiHelper.TextColored($"{SeverityBadge(entry.Severity)}  {entry.Heading}", color);
    ImGui.Separator();

    float lineH = ImGui.GetFrameHeightWithSpacing();
    float bodyH = lineH * 20f;
    float hintReserve = lineH * 1.8f;

    ImGui.BeginChild("##newsbody", new Vector2(0, bodyH), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

    bool needsScroll = false;
    ImGui.BeginChild("##newsdesc", new Vector2(0, bodyH - hintReserve));
    ImGui.PushTextWrapPos(0);
    ImGuiHelper.TextPretty(entry.LongDescription ?? entry.ShortDescription ?? "");
    needsScroll = ImGui.GetScrollMaxY() > 0.1f;
    ImGui.PopTextWrapPos();
    ImGui.EndChild();

    if (needsScroll)
    {
      ImGui.Dummy(new Vector2(0, 2));
      ImGuiHelper.TextColored("...scroll to continue reading...", ImGuiHelper.Green);
    }

    ImGui.EndChild();

    ImGui.Separator();

    if (actionCompleted)
    {
      // Post-action result view: big colored indicator + only a Close button
      var resultColor = actionSucceeded ? ImGuiHelper.Green : ImGuiHelper.Red;
      string icon = actionSucceeded ? "OK: " : "FAIL: ";
      string msg = string.IsNullOrEmpty(actionResultMessage)
        ? (actionSucceeded ? "Migration succeeded!" : "Migration failed. Check log.")
        : actionResultMessage;

      ImGui.Dummy(new Vector2(0, 2));
      ImGuiHelper.TextColored(icon + msg, resultColor);
      ImGui.Dummy(new Vector2(0, 6));

      if (ImGui.Button("Close", new Vector2(140, 36)))
      {
        if (detailIndex >= 0 && detailIndex < activeEntries.Count)
          HandleHandled(detailIndex, persist: false);
      }
    }
    else
    {
      bool canAct = !isActionBusy;

      var avail = ImGui.GetContentRegionAvail().x;
      var btnH = 36f;
      var spacing = ImGui.GetStyle().ItemSpacing.x;

      if (entry.Actions?.Primary != null)
      {
        var pw = Mathf.Min(260f, avail * 0.38f);
        if (ImGui.Button(entry.Actions.Primary.Label ?? "Continue", new Vector2(pw, btnH)) && canAct)
          OnAction(entry, entry.Actions.Primary, detailIndex);
        ImGui.SameLine();
        avail -= pw + spacing;
      }

      if (entry.Actions?.Secondary != null)
      {
        var sw = Mathf.Min(180f, avail * 0.32f);
        if (ImGui.Button(entry.Actions.Secondary.Label ?? "Details", new Vector2(sw, btnH)) && canAct)
          OnAction(entry, entry.Actions.Secondary, -1);
        ImGui.SameLine();
        avail -= sw + spacing;
      }

      var iw = Mathf.Min(120f, avail * 0.25f);
      if (ImGui.Button("Ignore", new Vector2(iw, btnH)) && canAct)
      {
        confirmIndex = detailIndex;
        showConfirm = true;
      }

      if (activeEntries.Count > 1 && !activeEntries.All(e => e.Type == "info"))
      {
        ImGui.SameLine();
        if (ImGui.Button("Back to list", new Vector2(140, btnH)))
        {
          detailIndex = -1;
          infoTimerStart = null;
          actionCompleted = false;
          actionSucceeded = false;
          actionResultMessage = null;
          actionStatus = null;
          isActionBusy = false;
        }
      }

      if (isActionBusy)
      {
        ImGui.SameLine();
        ImGuiHelper.TextDisabled("Working...");
      }

      if (!string.IsNullOrEmpty(actionStatus))
      {
        ImGui.Spacing();
        ImGuiHelper.TextError(actionStatus);
      }
    }

    if (entry.Type == "info" && infoTimerStart.HasValue)
    {
      var elapsed = (DateTime.UtcNow - infoTimerStart.Value).TotalSeconds;
      var rem = Math.Max(0, 10 - (int)elapsed);
      ImGuiHelper.TextDisabled($"This notice will auto-acknowledge in {rem}s");
    }
  }

  private static void DrawConfirmView()
  {
    ImGui.Dummy(new Vector2(0, 10));
    ImGuiHelper.TextWarning("Are you sure?");
    ImGui.Spacing();
    ImGuiHelper.Text("You will not be able to undo this action. Ignoring a notice may result in a broken mod.");
    ImGui.Spacing();
    ImGui.Separator();

    var cAvail = ImGui.GetContentRegionAvail().x;
    if (ImGui.Button("Yes, ignore this notice", new Vector2(cAvail * 0.55f, 36)))
    {
      if (confirmIndex >= 0 && confirmIndex < activeEntries.Count)
        HandleHandled(confirmIndex, persist: true);
    }
    ImGui.SameLine();
    if (ImGui.Button("Go back", new Vector2(cAvail * 0.35f, 36)))
    {
      showConfirm = false;
      confirmIndex = -1;
    }
  }

  private static void StartInfoTimerIfInfo(NewsEntry e)
  {
    infoTimerStart = (e.Type == "info") ? DateTime.UtcNow : null;
  }

  private static Color GetSeverityColor(string severity)
  {
    return (severity?.ToLowerInvariant()) switch
    {
      "critical" => ImGuiHelper.Red,
      "warning" => ImGuiHelper.Yellow,
      _ => ImGuiHelper.TextColor,
    };
  }

  private static string SeverityBadge(string severity)
  {
    return (severity?.ToLowerInvariant()) switch
    {
      "critical" => "CRITICAL",
      "warning" => "WARNING",
      _ => "INFO",
    };
  }

  private static void OnAction(NewsEntry entry, NewsAction action, int handledIndexIfDone)
  {
    if (action == null)
      return;

    if (action.Action == "open_url")
    {
      if (!string.IsNullOrEmpty(action.Url))
        Application.OpenURL(action.Url);
      return;
    }

    if (action.Action == "repo_mod_install")
    {
      isActionBusy = true;
      actionStatus = null;
      actionCompleted = false;
      actionSucceeded = false;
      actionResultMessage = null;
      _ = DoRepoModInstall(action);
      return;
    }

    if (action.Action == "workshop_mod_install")
    {
      isActionBusy = true;
      actionStatus = null;
      actionCompleted = false;
      actionSucceeded = false;
      actionResultMessage = null;
      _ = DoWorkshopModInstall(entry, action);
      return;
    }

    if (action.Action == "workshop_mod_subscribe")
    {
      isActionBusy = true;
      actionStatus = null;
      actionCompleted = false;
      actionSucceeded = false;
      actionResultMessage = null;
      _ = DoWorkshopModSubscribe(action);
      return;
    }

    if (action.Action == "acknowledge" || action.Action == "dismiss" || string.IsNullOrEmpty(action.Action))
    {
      // just close the notice and do nothing else. Default for info type notices.
      if (handledIndexIfDone >= 0)
        HandleHandled(handledIndexIfDone, persist: false);
      return;
    }

    // default treat as acknowledged
    if (handledIndexIfDone >= 0)
      HandleHandled(handledIndexIfDone, persist: false);
  }

  private static async UniTask DoRepoModInstall(NewsAction action)
  {
    actionStatus = null;
    try
    {
      bool ok = await NewsRunner.ExecuteRepoModInstall(action?.Url, action?.ModId);
      isActionBusy = false;
      actionCompleted = true;
      actionSucceeded = ok;
      actionResultMessage = ok ? "Migration succeeded!" : "Migration failed. Check log.";
    }
    catch (Exception ex)
    {
      Logger.Global.LogException(ex);
      isActionBusy = false;
      actionCompleted = true;
      actionSucceeded = false;
      actionResultMessage = "Migration failed. See log.";
    }
  }

  private static async UniTask DoWorkshopModInstall(NewsEntry entry, NewsAction action)
  {
    actionStatus = null;
    try
    {
      ulong? oldWid = null;
      if (ulong.TryParse(entry?.Trigger?.WorkshopId, out var t) && t > 1)
        oldWid = t;

      bool ok = await NewsRunner.ExecuteWorkshopModInstall(action?.WorkshopId, oldWid);
      isActionBusy = false;
      actionCompleted = true;
      actionSucceeded = ok;
      actionResultMessage = ok ? "Migration succeeded!" : "Migration failed. Check log.";
    }
    catch (Exception ex)
    {
      Logger.Global.LogException(ex);
      isActionBusy = false;
      actionCompleted = true;
      actionSucceeded = false;
      actionResultMessage = "Migration failed. See log.";
    }
  }

  private static async UniTask DoWorkshopModSubscribe(NewsAction action)
  {
    actionStatus = null;
    try
    {
      bool ok = await NewsRunner.ExecuteWorkshopModInstall(action?.WorkshopId);
      isActionBusy = false;
      actionCompleted = true;
      actionSucceeded = ok;
      actionResultMessage = ok ? "Workshop mod installed!" : "Workshop install failed. Check log.";
    }
    catch (Exception ex)
    {
      Logger.Global.LogException(ex);
      isActionBusy = false;
      actionCompleted = true;
      actionSucceeded = false;
      actionResultMessage = "Workshop install failed. See log.";
    }
  }

  private static void HandleHandled(int index, bool persist)
  {
    if (index < 0 || index >= activeEntries.Count)
      return;

    var e = activeEntries[index];
    if (persist && !string.IsNullOrEmpty(e.Id))
      NewsDismissal.Dismiss(e.Id);

    activeEntries.RemoveAt(index);
    isActionBusy = false;
    actionStatus = null;
    actionCompleted = false;
    actionSucceeded = false;
    actionResultMessage = null;
    infoTimerStart = null;
    showConfirm = false;
    confirmIndex = -1;
    detailIndex = -1;

    if (activeEntries.Count == 0)
      visible = false;
  }
}
