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

    bool useList = activeEntries.Count > 1 && detailIndex < 0 && !showConfirm;

    var screen = ImGuiHelper.ScreenRect();
    var w = screen.Size.x * 0.58f;
    var maxH = screen.Size.y * 0.8f;

    var size = new Vector2(w, 0);  // height 0 = size to content 
    ImGui.SetNextWindowSize(size);
    ImGui.SetNextWindowPos(new Vector2((screen.Size.x - w) * 0.5f, screen.Size.y * 0.1f));
    ImGui.SetNextWindowFocus();

    ImGui.SetNextWindowSizeConstraints(
        new Vector2(500, 350),
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

    ImGui.End();

    CheckInfoAutoAdvance();
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
    ImGuiHelper.Text("Multiple notices require attention. Select one to view details.");
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
      }
      ImGui.PopStyleColor();
      if (ImGui.IsItemHovered())
        ImGuiHelper.TextTooltip("Click to view details");
    }

    ImGui.EndChild();
    ImGui.Separator();
    ImGuiHelper.TextDisabled("You must view and handle all notices before the game can continue loading.");
  }

  private static void DrawDetailView()
  {
    if (detailIndex < 0 || detailIndex >= activeEntries.Count)
      detailIndex = 0;

    var entry = activeEntries[detailIndex];

    var color = GetSeverityColor(entry.Severity);
    ImGuiHelper.TextColored($"{SeverityBadge(entry.Severity)}  {entry.Heading}", color);
    ImGui.Separator();
    ImGuiHelper.TextPretty(entry.LongDescription ?? entry.ShortDescription ?? "");
    ImGui.Dummy(new Vector2(0, 20)); // extra breathing room after text

    ImGui.Separator();

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
      {
        _ = NewsRunner.ExecuteSecondaryAction(entry);
      }
      ImGui.SameLine();
      avail -= sw + spacing;
    }

    var iw = Mathf.Min(120f, avail * 0.25f);
    if (ImGui.Button("Ignore", new Vector2(iw, btnH)) && canAct)
    {
      confirmIndex = detailIndex;
      showConfirm = true;
    }

    if (activeEntries.Count > 1)
    {
      ImGui.SameLine();
      if (ImGui.Button("Back to list", new Vector2(140, btnH)))
      {
        detailIndex = -1;
        infoTimerStart = null;
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
    ImGuiHelper.Text("This notice was shown for a reason. Ignoring it may result in a broken or non-functional mod. Do you still want to ignore this?");
    ImGui.Spacing();
    ImGui.Separator();

    var cAvail = ImGui.GetContentRegionAvail().x;
    if (ImGui.Button("Yes, ignore this notice", new Vector2(cAvail * 0.55f, 36)))
    {
      if (confirmIndex >= 0 && confirmIndex < activeEntries.Count)
        HandleHandled(confirmIndex, persist: true);
    }
    ImGui.SameLine();
    if (ImGui.Button("Cancel", new Vector2(cAvail * 0.35f, 36)))
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
      _ = DoRepoModInstall(entry, action, handledIndexIfDone);
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

  private static async UniTask DoRepoModInstall(NewsEntry entry, NewsAction action, int index)
  {
    actionStatus = null;
    try
    {
      bool ok = await NewsRunner.ExecuteRepoModInstall(action?.Url, action?.ModId);
      if (ok)
      {
        if (index >= 0)
          HandleHandled(index, persist: false);
      }
      else
      {
        actionStatus = "Migration install failed. Check log.";
        isActionBusy = false;
      }
    }
    catch (Exception ex)
    {
      Logger.Global.LogException(ex);
      actionStatus = "Install failed. See log.";
      isActionBusy = false;
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
    infoTimerStart = null;
    showConfirm = false;
    confirmIndex = -1;
    detailIndex = -1;

    if (activeEntries.Count == 0)
      visible = false;
  }
}
