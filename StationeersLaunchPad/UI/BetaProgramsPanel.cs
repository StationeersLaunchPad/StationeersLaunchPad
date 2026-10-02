using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using ImGuiNET;
using StationeersLaunchPad.Metadata;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public static class BetaProgramsPanel
{
  private static readonly HashSet<ulong> operations = [];
  private static readonly Dictionary<ulong, string> statuses = [];

  public static bool Busy => operations.Count > 0;

  // beta switch for the mod shown in Mod Info, works from either the stable or the beta copy
  public static bool DrawModControls(LoadStage stage, ModList modList, ModInfo mod)
  {
    var stable = mod.HasBetaProgram ? mod
      : modList.IsBetaMod(mod) ? modList.AllMods.FirstOrDefault(other => other.IsBetaProgramFor(mod))
      : null;
    if (stable == null)
      return false;

    var changed = false;
    var beta = modList.AllMods.FirstOrDefault(other => other.WorkshopHandle == stable.BetaWorkshopHandle);
    var busy = operations.Contains(stable.BetaWorkshopHandle);
    ImGui.PushID($"beta-{stable.BetaWorkshopHandle}");
    ImGui.BeginDisabled(stage != LoadStage.Configuring || busy);
    if (beta == null)
    {
      if (ImGui.Button(busy ? "Downloading beta..." : "Subscribe to beta"))
        SubscribeToBeta(stable, modList).Forget();
    }
    else
    {
      var useBeta = beta.Enabled;
      if (ImGui.Checkbox($"Use beta v{beta.About?.Version ?? "?"}", ref useBeta))
        changed = SetBetaEnabled(stable, beta, modList, useBeta);
    }
    ImGui.EndDisabled();
    ImGuiHelper.ItemTooltip(stage == LoadStage.Configuring
      ? "Betas are separate Workshop items. Switching subscribes or unsubscribes them for you."
      : "Betas can only be switched before mods load.", hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled);
    if (statuses.TryGetValue(stable.BetaWorkshopHandle, out var status))
    {
      ImGui.SameLine();
      ImGuiHelper.TextDisabled(status);
    }
    ImGui.PopID();
    return changed;
  }

  public static bool SetModEnabled(ModList modList, ModInfo mod, bool enabled)
  {
    if (modList.IsBetaMod(mod))
    {
      var stable = modList.AllMods.FirstOrDefault(stable => stable.IsBetaProgramFor(mod));
      if (stable != null)
        return SetBetaEnabled(stable, mod, modList, enabled);
    }
    else if (enabled && mod.HasBetaProgram)
    {
      var beta = modList.AllMods.FirstOrDefault(beta =>
        beta.WorkshopHandle == mod.BetaWorkshopHandle);
      if (beta?.Enabled == true)
        return SetBetaEnabled(mod, beta, modList, false);
    }

    mod.Enabled = enabled;
    return true;
  }

  private static bool SetBetaEnabled(ModInfo stable, ModInfo beta, ModList modList, bool enabled)
  {
    if (operations.Contains(beta.WorkshopHandle))
      return false;

    stable.Enabled = !enabled;
    beta.Enabled = enabled;
    Logger.Global.LogInfo($"Switched {stable.Name} to {(enabled ? "beta" : "stable")}");
    if (enabled)
      statuses.Remove(beta.WorkshopHandle);
    else
      UnsubscribeFromBeta(stable, beta, modList).Forget();
    return true;
  }

  private static async UniTask SubscribeToBeta(ModInfo stable, ModList modList)
  {
    var workshopId = stable.BetaWorkshopHandle;
    if (!operations.Add(workshopId))
      return;

    statuses[workshopId] = "Subscribing and downloading...";
    try
    {
      if (!await Steam.SubscribeAndDownload(workshopId))
      {
        statuses[workshopId] = "Subscription or download failed. See logs for details.";
        return;
      }

      stable.Enabled = false;
      ModConfigUtil.SaveConfig(modList.ToModConfig());
      statuses.Remove(workshopId);
      LaunchPadConfig.ReloadMods();
    }
    finally
    {
      operations.Remove(workshopId);
    }
  }

  private static async UniTask UnsubscribeFromBeta(ModInfo stable, ModInfo beta, ModList modList)
  {
    var workshopId = beta.WorkshopHandle;
    if (!operations.Add(workshopId))
      return;

    stable.Enabled = true;
    beta.Enabled = false;
    ModConfigUtil.SaveConfig(modList.ToModConfig());
    statuses[workshopId] = "Unsubscribing from beta...";
    try
    {
      if (!await Steam.Unsubscribe(workshopId))
      {
        statuses[workshopId] = "Unsubscribe failed. The beta remains disabled.";
        return;
      }

      statuses.Remove(workshopId);
      LaunchPadConfig.ReloadMods();
    }
    finally
    {
      operations.Remove(workshopId);
    }
  }
}
