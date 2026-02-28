
using Cysharp.Threading.Tasks;
using StationeersLaunchPad.Update;
using System;

namespace StationeersLaunchPad.Commands
{
  public class SelfUpdateCommand : SubCommand
  {
    public SelfUpdateCommand() : base("selfupdate") { }
    public override string UsageDescription => "[<tag>] [force]";

    protected override CommandStage LeafStage => CommandStage.Init;
    protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
    {
      if (!ArgP(args).Flag("force", out var force).Positional(out var tag, null).Validate())
      {
        result = null;
        return false;
      }

      if (LaunchPadPaths.InstallDir == null)
      {
        result = "Invalid install dir. Cannot update";
        return true;
      }

      SLPCommand.AsyncCommand(Update(tag, force)).Forget();
      result = null;
      return true;
    }

    private static async UniTask Update(string tag, bool force)
    {
      var release = tag == null
        ? await Github.LaunchPadRepo.FetchLatestRelease()
        : await Github.LaunchPadRepo.FetchTagRelease(tag);
      if (release == null)
      {
        Print($"Could not find StationeersLaunchPad release {tag}");
        return;
      }
      var cmp = Version.Compare(release.TagName, LaunchPadInfo.VERSION);
      if (!force && cmp <= 0)
      {
        Print($"StationeersLaunchPad version {LaunchPadInfo.VERSION} >= {release.TagName}. Skipping update.");
        return;
      }
      if (!await LaunchPadUpdater.UpdateToRelease(release))
      {
        Print($"Failed to update to {release.TagName}");
        return;
      }
      if (cmp < 0 && Configs.AutoUpdateOnStart.Value)
      {
        Print($"StationeersLaunchPad version downgraded. Disabling AutoUpdateOnStart");
        Configs.AutoUpdateOnStart.Value = false;
      }
      await Platform.ContinueAfterUpdate();
    }
  }
}