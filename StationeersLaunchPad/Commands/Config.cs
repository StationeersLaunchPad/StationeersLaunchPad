
using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StationeersLaunchPad.Commands
{
  public class ConfigCommand : SubCommand
  {
    public ConfigCommand() : base("config", new ListCommand(), new SetCommand()) { }

    public override string UsageDescription =>
      "list or set LaunchPad configuration settings";

    private static string ConfigName(ConfigDefinition def)
    {
      if (def.Section.Contains(' ') || def.Key.Contains(' '))
        return $"\"{def}\"";
      return $"{def}";
    }

    public class ListCommand : SubCommand
    {
      public ListCommand() : base("list") { }
      public override string UsageDescription => "[<searchtext>]";

      protected override CommandStage LeafStage => CommandStage.ConfigLoaded;
      protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
      {
        if (!ArgP(args).Positional(out var filter, "").Validate())
        {
          result = null;
          return false;
        }
        var parts = filter.Split('.', 2);
        var (section, key) = parts.Length == 2 ? (parts[0], parts[1]) : (null, filter);

        var sb = new StringBuilder();
        foreach (var category in Configs.Sorted.Categories)
        {
          if (section != null &&
              !category.Category.Contains(section, StringComparison.OrdinalIgnoreCase))
            continue;
          var fullCat = section == null &&
            category.Category.Contains(key, StringComparison.OrdinalIgnoreCase);
          foreach (var cfg in category.Entries)
          {
            var def = cfg.Definition;
            if (!fullCat && !def.Key.Contains(key, StringComparison.OrdinalIgnoreCase))
              continue;
            sb.AppendLine($"{ConfigName(def)}={cfg.Entry.GetSerializedValue()}");
          }
        }

        result = sb.ToString().TrimEnd();
        return true;
      }
    }

    public class SetCommand : SubCommand
    {
      public SetCommand() : base("set") { }
      public override string UsageDescription => "<name> <value>";

      protected override CommandStage LeafStage => CommandStage.ConfigLoaded;
      protected override bool RunLeaf(ReadOnlySpan<string> args, out string result)
      {
        if (!ArgP(args).Positional(out var name).Positional(out var value).Validate())
        {
          result = null;
          return false;
        }

        var parts = name.Split('.', 2);
        var (section, key) = parts.Length == 2 ? (parts[0], parts[1]) : (null, name);

        var matches = new List<ConfigEntryBase>();
        foreach (var category in Configs.Sorted.Categories)
        {
          if (section != null && !section.Equals(category.Category, StringComparison.OrdinalIgnoreCase))
            continue;
          foreach (var cfg in category.Entries)
          {
            if (cfg.Definition.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
              matches.Add(cfg.Entry);
          }
        }
        if (matches.Count == 0)
        {
          result = $"No configs found for \"{name}\"";
          return true;
        }

        if (matches.Count > 1)
        {
          var matchstr = string.Join(", ", matches.Select(cfg => $"\"{cfg.Definition}\""));
          result = $"\"{name}\" is ambiguous between {matchstr}";
          return true;
        }

        var prevVal = matches[0].GetSerializedValue();
        matches[0].SetSerializedValue(value);
        var newVal = matches[0].GetSerializedValue();
        result = $"Changed \"{matches[0].Definition}\" from \"{prevVal}\" to \"{newVal}\"";
        return true;
      }
    }
  }
}