using System;
using System.Collections.Generic;
using System.Linq;

namespace StationeersLaunchPad.News;

public static class NewsDismissal
{
  public static HashSet<string> LoadDismissed()
  {
    var raw = Configs.NewsDismissedIds?.Value ?? "";
    return Parse(raw);
  }

  public static void Dismiss(string id)
  {
    if (string.IsNullOrWhiteSpace(id))
      return;

    var set = LoadDismissed();
    if (!set.Add(id))
      return;

    Configs.NewsDismissedIds.Value = string.Join(",", set);
  }

  public static bool IsDismissed(string id)
  {
    if (string.IsNullOrWhiteSpace(id))
      return false;
    return LoadDismissed().Contains(id);
  }

  private static HashSet<string> Parse(string raw)
  {
    var set = new HashSet<string>(StringComparer.Ordinal);
    if (string.IsNullOrWhiteSpace(raw))
      return set;

    foreach (var part in raw.Split(','))
    {
      var trimmed = part.Trim();
      if (trimmed.Length > 0)
        set.Add(trimmed);
    }
    return set;
  }
}
