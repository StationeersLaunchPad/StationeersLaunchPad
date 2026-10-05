using System;
using System.Collections.Generic;
using System.Linq;

namespace StationeersLaunchPad.Metadata;

// trusted modders on the Stationeers Modding Discord, matched by the author name in About.xml
public static class TrustedModders
{
  private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
  {
    "Lorexcold", "Wikus", "JXSN", "JacksonTheMaster", "Elmotrix", "FlorpyDorp", "Monica", "Sukasa",
    "Vivien", "Beef", "TheRealBeef", "Miles", "Aproposmath", "Emily", "Inaki", "Ilodev",
    "tom_is_unlucky", "Chipstix", "Chipstix213", "ViroMan", "Dipole", "rocket2guns",
  };

  private static readonly string[] Separators = [",", "&", " and "];

  // true when any of the listed authors is trusted
  public static bool IsTrusted(string author) =>
    !string.IsNullOrWhiteSpace(author)
    && author.Split(Separators, StringSplitOptions.RemoveEmptyEntries).Any(name => Names.Contains(name.Trim()));
}
