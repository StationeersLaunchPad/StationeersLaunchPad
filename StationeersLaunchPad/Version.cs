
using System;

namespace StationeersLaunchPad
{
  public static class Version
  {
    public const char SECTION_SEPARATOR = '.';
    public const char PART_SEPARATOR = '-';

    public static int Compare(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
      var lreader = new VersionReader(left);
      var rreader = new VersionReader(right);

      while (!lreader.Done && !rreader.Done)
      {
        var l = lreader.ReadPart();
        var r = rreader.ReadPart();

        var cmp = -l.Section.CompareTo(r.Section);
        if (cmp != 0) return cmp;
        cmp = l.Prefix.CompareToInvariant(r.Prefix);
        if (cmp != 0) return cmp;
        cmp = l.Value.CompareTo(r.Value);
        if (cmp != 0) return cmp;
        cmp = l.Suffix.CompareToInvariant(r.Suffix);
        if (cmp != 0) return cmp;
      }

      if (!lreader.Done)
        return 1;
      if (!rreader.Done)
        return -1;
      return 0;
    }

    private ref struct Part
    {
      public int Section;
      public ReadOnlySpan<char> Prefix;
      public ulong Value;
      public ReadOnlySpan<char> Suffix;

      public override string ToString() =>
        $"{Section} '{Prefix.ToString()}' {Value} '{Suffix.ToString()}'";
    }

    private ref struct VersionReader
    {
      private ReadOnlySpan<char> data;
      private int section;
      private bool newSection;

      public VersionReader(ReadOnlySpan<char> data)
      {
        if (data.Length > 0 && data[0] is 'v' or 'V')
          data = data[1..];
        this.data = data;
        section = 0;
        newSection = true;
      }

      public bool Done => data.Length == 0 && !newSection;

      public Part ReadPart()
      {
        newSection = false;
        var len = 0;
        while (len < data.Length && data[len] is not (SECTION_SEPARATOR or PART_SEPARATOR))
          len++;

        var curSection = section;
        var spart = data[..len];
        data = data[len..];
        if (data.Length > 0)
        {
          if (data[0] is SECTION_SEPARATOR)
          {
            section++;
            newSection = true;
          }
          data = data[1..];
        }

        var part = new Part { Section = curSection };
        if (len > 0 && char.IsDigit(spart[0]))
        {
          var valLen = 1;
          while (valLen < spart.Length && char.IsDigit(spart[valLen]))
            valLen++;
          ulong.TryParse(spart[..valLen], out part.Value);
          part.Suffix = spart[valLen..];
        }
        else if (len > 0 && char.IsDigit(spart[^1]))
        {
          var valLen = 1;
          while (valLen < len && char.IsDigit(data[len - valLen - 1]))
            valLen++;
          ulong.TryParse(spart[(len - valLen)..], out part.Value);
          part.Prefix = spart[..(len - valLen)];
        }
        else
          part.Prefix = spart;
        return part;
      }
    }
  }
}