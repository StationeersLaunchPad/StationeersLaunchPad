
using Assets.Scripts;
using StationeersLaunchPad.UI;
using Util.Commands;

namespace StationeersLaunchPad
{
  public class SLPCommand : CommandBase
  {
    public override string HelpText => string.Join('\n',
      "StationeersLaunchPad Commands",
      "\tmodpkg -- generate mod package for dedicated server or debugging",
      "\tlogs -- open mod logs window"
    );

    public override string[] Arguments => new string[] { "modpkg | logs" };

    public override bool IsLaunchCmd => true;

    public override string Execute(string[] args)
    {
      if (args.Length == 0)
        return HelpText;
      return args[0].ToLowerInvariant() switch
      {
        "modpkg" => ModPkg(),
        "logs" => Logs(),
        _ => HelpText,
      };
    }

    private string ModPkg() => LaunchPadConfig.ExportModPackage();

    private string Logs()
    {
      LogPanel.OpenStandaloneLogs();
      ConsoleWindow.Hide();
      return null;
    }
  }
}