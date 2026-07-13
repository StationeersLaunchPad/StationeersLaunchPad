using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using ImGuiNET;
using StationeersLaunchPad.Metadata;

namespace StationeersLaunchPad.UI;

public static class BetaProgramsPanel
{
  private static readonly HashSet<ulong> operations = [];
  private static readonly Dictionary<ulong, string> statuses = [];

  public static bool Busy => operations.Count > 0;

  public static bool Draw(LoadStage stage, ModList modList)
  {
    var changed = false;
    if (!ImGui.BeginTabItem("Betas"))
      return changed;

    var stableMods = modList.AllMods
      .Where(mod => mod.HasBetaProgram)
      .GroupBy(mod => mod.BetaWorkshopHandle)
      .Select(mods => mods.FirstOrDefault(mod => mod.Enabled) ?? mods.First())
      .ToList();
    var betaMods = modList.AllMods.Where(modList.IsBetaMod).ToList();

    ImGuiHelper.TextDisabled("Switch installed mods between their stable and beta Workshop versions.");
    ImGui.BeginDisabled(stage != LoadStage.Configuring || Busy);
    if (ImGui.Button("Refresh Subscribed Betas"))
      LaunchPadConfig.ReloadMods();
    ImGui.EndDisabled();
    ImGuiHelper.ItemTooltip(stage == LoadStage.Configuring
      ? "Reload the current local and workshop mod list."
      : "The mod list can only be refreshed before loading mods.",
      hoverFlags: ImGuiHoveredFlags.AllowWhenDisabled);

    if (stableMods.Count == 0 && betaMods.Count == 0)
    {
      ImGui.Spacing();
      ImGuiHelper.TextDisabled("No installed mods advertise a beta program.");
    }

    foreach (var stable in stableMods)
    {
      var beta = modList.AllMods.FirstOrDefault(mod =>
        mod.WorkshopHandle == stable.BetaWorkshopHandle);
      var busy = operations.Contains(stable.BetaWorkshopHandle);

      ImGui.PushID($"beta-{stable.BetaWorkshopHandle}");
      ImGui.Spacing();
      ImGuiHelper.Text(stable.Name);
      ImGuiHelper.TextDisabled($"Stable: {stable.About?.Version ?? "Unknown"} ({stable.WorkshopHandle})");
      ImGuiHelper.TextDisabled(beta == null
        ? $"Beta: not subscribed ({stable.BetaWorkshopHandle})"
        : $"Beta: {beta.About?.Version ?? "Unknown"} ({beta.WorkshopHandle})");

      if (beta == null)
      {
        ImGui.BeginDisabled(stage != LoadStage.Configuring || busy);
        if (ImGui.Button(busy ? "Downloading..." : "Subscribe to Beta"))
          SubscribeToBeta(stable, modList).Forget();
        ImGui.EndDisabled();
      }
      else
      {
        var useBeta = beta.Enabled;
        ImGui.BeginDisabled(stage != LoadStage.Configuring || busy);
        if (ImGui.Checkbox("Use Beta Version", ref useBeta))
          changed = SetBetaEnabled(stable, beta, modList, useBeta);
        ImGui.EndDisabled();
      }

      ImGui.SameLine();
      if (ImGui.Button("Open Beta Workshop Page"))
        Steam.OpenWorkshopPage(stable.BetaWorkshopHandle);
      if (stable.WorkshopHandle > 1)
      {
        ImGui.SameLine();
        if (ImGui.Button("Open Stable Workshop Page"))
          Steam.OpenWorkshopPage(stable.WorkshopHandle);
      }

      if (beta?.Enabled == true && stable.Enabled)
        ImGuiHelper.TextWarning("Stable and beta are both enabled.");
      else if (beta?.Enabled == true)
        ImGuiHelper.TextWarning("Beta is active.");
      else if (stable.Enabled)
        ImGuiHelper.TextDisabled("Stable is active.");
      else if (beta != null)
        ImGuiHelper.TextDisabled("Both versions are disabled.");
      else
        ImGuiHelper.TextDisabled("Stable is disabled.");

      if (statuses.TryGetValue(stable.BetaWorkshopHandle, out var status))
        ImGuiHelper.TextDisabled(status);
      ImGui.Separator();
      ImGui.PopID();
    }

    var linkedBetaHandles = stableMods.Select(mod => mod.BetaWorkshopHandle).ToHashSet();
    var unlinkedBetas = betaMods.Where(mod => !linkedBetaHandles.Contains(mod.WorkshopHandle)).ToList();
    if (unlinkedBetas.Count > 0)
    {
      ImGui.Spacing();
      ImGuiHelper.Text("Other beta program mods");
      foreach (var beta in unlinkedBetas)
        ImGuiHelper.TextDisabled($"{beta.Name} {beta.About?.Version} ({(beta.Enabled ? "Active" : "Disabled")})");
    }

    ImGui.EndTabItem();
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
