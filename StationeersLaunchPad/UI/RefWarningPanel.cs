
using Assets.Scripts;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using ImGuiNET;
using StationeersLaunchPad.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace StationeersLaunchPad.UI
{
  public static class RefWarningPanel
  {
    private static Harmony harmony;
    private static bool visible = false;
    private static List<ModInfo> offenders;

    private static void EnsurePatch()
    {
      harmony ??= new("SLPRefWarning");

      try
      {
        var patchMethod = typeof(RefWarningPanel).GetMethod(nameof(Draw));
        foreach (var method in new MethodBase[]
        {
          typeof(SplashBehaviour).GetMethod(nameof(SplashBehaviour.Draw)),
          typeof(OrbitalSimulation).GetMethod(nameof(OrbitalSimulation.Draw)),
        })
        {
          var info = Harmony.GetPatchInfo(method);
          if (info.Postfixes.Any(patch => patch.PatchMethod == patchMethod))
            continue;
          harmony.Patch(method, postfix: new(patchMethod));
        }
      }
      catch (Exception) { }
    }

    public static async UniTask Show(List<ModInfo> offendingMods)
    {
      if (Platform.IsServer)
        return;
      EnsurePatch();
      offenders = offendingMods;
      visible = true;
      while (visible)
        await UniTask.Yield();
    }

    public static void Draw()
    {
      if (!visible)
        return;
      ImGuiHelper.Draw(() =>
      {
        const string modalName = "Unsupported Mods##SLPRefWarning";
        ImGui.OpenPopup(modalName);
        ImGui.BeginPopupModal(modalName, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize);

        ImGuiHelper.Text("The following mods contain unsupported references to StationeersLaunchPad");
        ImGuiHelper.Text("and may break whenever StationeersLaunchPad updates.");
        ImGuiHelper.Text("If an error is encountered, please disable these and try again");
        ImGuiHelper.Text("before reporting bugs in StationeersLaunchPad");
        ImGui.Separator();
        foreach (var mod in offenders)
        {
          ImGui.Bullet();
          ImGui.SameLine();
          ImGuiHelper.Text(mod.Name);
          ImGui.SameLine();
          ImGuiHelper.Text("by");
          ImGui.SameLine();
          ImGuiHelper.Text(mod.About.Author);
        }

        if (ImGui.Button("OK", new(ImGui.GetContentRegionAvail().x, ImGui.GetTextLineHeightWithSpacing())))
          visible = false;
        ImGui.EndPopup();
      });
    }
  }
}