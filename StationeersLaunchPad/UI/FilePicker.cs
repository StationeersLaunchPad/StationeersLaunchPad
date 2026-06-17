using System;
using System.IO;
using System.Linq;
using ImGuiNET;
using UnityEngine;

namespace StationeersLaunchPad.UI;

// A modal folder/file browser drawn on top of the loader UI with a darkened background.
// Save mode lets the user pick a destination folder + file name; Open mode lets the user
// pick an existing file. The chosen full path is handed back via the onConfirm callback.
public static class FilePicker
{
  private static bool active;
  private static bool saveMode;

  private static string title;
  private static string currentDir;
  private static string fileName;
  private static string selectedFile;
  private static string filter;
  private static Action<string> onConfirm;

  public static bool IsOpen => active;

  public static void OpenSave(string title, string startDir, string defaultName, string filter, Action<string> onConfirm)
    => Open(title, startDir, defaultName, filter, false, onConfirm);

  public static void OpenLoad(string title, string startDir, string filter, Action<string> onConfirm)
    => Open(title, startDir, null, filter, true, onConfirm);

  private static void Open(string title, string startDir, string defaultName, string filter, bool open, Action<string> onConfirm)
  {
    FilePicker.title = title;
    FilePicker.filter = filter;
    FilePicker.onConfirm = onConfirm;
    saveMode = !open;
    fileName = defaultName ?? "";
    selectedFile = null;
    currentDir = ResolveStartDir(startDir);
    active = true;
  }

  private static string ResolveStartDir(string dir)
  {
    try
    {
      if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        return Path.GetFullPath(dir);
    }
    catch { }
    return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
  }

  public static void Draw()
  {
    if (!active)
      return;

    ImGuiHelper.Draw(() =>
    {
      var screen = ImGuiHelper.ScreenRect();

      // A single full-screen window IS the darkened backdrop. The dialog is a centered child
      // inside it, so there is only one interactive window (like AlertPopup) - this keeps the
      // child's widgets clickable while the backdrop absorbs clicks outside the dialog.
      ImGui.SetNextWindowPos(screen.Min);
      ImGui.SetNextWindowSize(screen.Size);
      ImGui.SetNextWindowFocus();
      ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
      ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
      ImGui.PushStyleColor(ImGuiCol.WindowBg, (Vector4)new Color(0f, 0f, 0f, 0.6f));
      ImGui.Begin("##pickeroverlay",
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
        | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);
      ImGui.PopStyleColor();
      ImGui.PopStyleVar(2);

      var size = new Vector2(760, 540);
      ImGui.SetCursorScreenPos(screen.Min + (screen.Size - size) / 2f);

      ImGui.PushStyleColor(ImGuiCol.ChildBg, (Vector4)new Color(0.07f, 0.07f, 0.09f, 0.98f));
      ImGui.BeginChild("##pickerpanel", size, true);
      ImGui.PopStyleColor();

      ImGuiHelper.Text(title);
      ImGui.Separator();
      DrawContent();

      ImGui.EndChild();
      ImGui.End();
    });
  }

  private static void DrawContent()
  {
    ImGui.AlignTextToFramePadding();
    ImGuiHelper.Text("Location:");
    ImGui.SameLine();
    ImGuiHelper.TextDisabled(currentDir);

    // Drive shortcuts.
    foreach (var drive in SafeDrives())
    {
      ImGui.SameLine();
      if (ImGui.Button(drive))
        Navigate(drive);
    }

    ImGui.Separator();

    var spacing = ImGui.GetStyle().ItemSpacing.y;
    var bottomHeight = ImGui.GetFrameHeightWithSpacing()
      + (saveMode ? ImGui.GetFrameHeightWithSpacing() : 0f)
      + spacing + 1f;
    var listSize = new Vector2(0f, ImGui.GetContentRegionAvail().y - bottomHeight);

    string navTo = null;
    ImGui.BeginChild("##picklist", listSize, true);

    var parent = SafeParent(currentDir);
    if (parent != null && ImGui.Selectable("[..]"))
      navTo = parent;

    foreach (var dir in SafeDirectories(currentDir))
      if (ImGui.Selectable($"[D] {Path.GetFileName(dir)}"))
        navTo = dir;

    if (saveMode == false)
    {
      foreach (var file in SafeFiles(currentDir))
      {
        var name = Path.GetFileName(file);
        if (ImGui.Selectable(name, selectedFile == file))
          selectedFile = file;
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
          selectedFile = file;
          Confirm();
        }
      }
    }

    ImGui.EndChild();

    if (navTo != null)
    {
      currentDir = navTo;
      selectedFile = null;
    }

    ImGui.Separator();

    if (saveMode)
    {
      ImGui.AlignTextToFramePadding();
      ImGuiHelper.Text("File name:");
      ImGui.SameLine();
      ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().x);
      ImGui.InputText("##filename", ref fileName, 256);
    }

    var canConfirm = saveMode ? !string.IsNullOrWhiteSpace(fileName) : selectedFile != null;
    ImGui.BeginDisabled(!canConfirm);
    if (ImGui.Button(saveMode ? "Save" : "Open", new Vector2(120, 0)))
      Confirm();
    ImGui.EndDisabled();

    ImGui.SameLine();
    if (ImGui.Button("Cancel", new Vector2(120, 0)))
      Close();
  }

  private static void Confirm()
  {
    string path;
    try
    {
      path = saveMode ? Path.Combine(currentDir, fileName.Trim()) : selectedFile;
    }
    catch (Exception ex)
    {
      Logger.Global.LogException(ex);
      Close();
      return;
    }

    var callback = onConfirm;
    Close();
    callback?.Invoke(path);
  }

  private static void Close()
  {
    active = false;
    onConfirm = null;
    selectedFile = null;
  }

  private static void Navigate(string dir)
  {
    currentDir = dir;
    selectedFile = null;
  }

  private static string[] SafeDrives()
  {
    try
    {
      return DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name).ToArray();
    }
    catch { return []; }
  }

  private static string SafeParent(string dir)
  {
    try { return Directory.GetParent(dir)?.FullName; }
    catch { return null; }
  }

  private static string[] SafeDirectories(string dir)
  {
    try
    {
      return Directory.GetDirectories(dir)
        .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
        .ToArray();
    }
    catch { return []; }
  }

  private static string[] SafeFiles(string dir)
  {
    try
    {
      return Directory.GetFiles(dir, $"*{filter}")
        .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
        .ToArray();
    }
    catch { return []; }
  }
}
