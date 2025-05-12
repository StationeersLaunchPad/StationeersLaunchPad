using System;
using System.Collections.Generic;
using System.Text;

namespace StationeersLaunchPad
{
  public static class TextFormatting
  {
    public static string SteamToTMP(string text)
    {
      var res = new StringBuilder();
      var index = 0;
      var openTags = new Stack<Range>();
      var closeTags = new Stack<string>();

      void appendRange(Range range)
      {
        for (var i = range.Start; i < range.End; i++)
        {
          res.Append(text[i]);
        }
      }
      bool rangesEqual(Range r1, Range r2)
      {
        if (r1.Length != r2.Length)
          return false;
        for (var i = 0; i < r1.Length; i++)
          if (text[r1.Start + i] != text[r2.Start + i])
            return false;
        return true;
      }
      bool rangeIs(Range range, string str)
      {
        if (range.Length != str.Length)
          return false;
        for (var i = 0; i < range.Length; i++)
          if (text[range.Start + i] != str[i])
            return false;
        return true;
      }
      bool closeTag(Range name)
      {
        if (openTags.Count == 0)
          return false;
        var top = openTags.Peek();
        if (!rangesEqual(name, top))
          return false;
        openTags.Pop();
        res.Append(closeTags.Pop());
        return true;
      }
      bool tryOpenTag(Range name, string match, string open, string close)
      {
        if (!rangeIs(name, match))
          return false;
        res.Append(open);
        openTags.Push(name);
        closeTags.Push(close);
        return true;
      }
      void openTag(Range name, string close)
      {
        openTags.Push(name);
        closeTags.Push(close);
      }
      string rangeString(Range range)
      {
        return text.Substring(range.Start, range.Length);
      }

      var ignoreWs = false;
      var codeBlocks = 0;
      var noParse = false;

      Consume(text, ref index);
      while (index < text.Length)
      {
        var el = ParseElement(text, ref index);

        switch (el.Type)
        {
          case ElementType.Whitespace:
            if (ignoreWs)
              break;
            if (codeBlocks > 0)
              appendRange(el.Full);
            else
              res.Append(' ');
            break;
          case ElementType.Newline:
            res.AppendLine();
            if (codeBlocks == 0)
              ignoreWs = true;
            break;
          case ElementType.Text:
            ignoreWs = false;
            appendRange(el.Full);
            break;
          case ElementType.OpenTag:
            // skip all open tags in noparse
            if (noParse)
            {
              appendRange(el.Full);
              break;
            }
            // simple and ignored tags
            if (
              tryOpenTag(el.TagName, "b", "<b>", "</b>") ||
              tryOpenTag(el.TagName, "i", "<i>", "</i>") ||
              tryOpenTag(el.TagName, "u", "<u>", "</u>") ||
              tryOpenTag(el.TagName, "strike", "<s>", "</s>") ||
              tryOpenTag(el.TagName, "spoiler", "", "") ||
              tryOpenTag(el.TagName, "h1", "<size=166%><color=#5aa9d6>", "</color></size>") ||
              tryOpenTag(el.TagName, "h2", "<size=150%><color=#5aa9d6>", "</color></size>") ||
              tryOpenTag(el.TagName, "h3", "<size=133%><color=#5aa9d6>", "</color></size>")
            )
              break;
            else if (rangeIs(el.TagName, "url"))
            {
              res.Append($"<link=\"{rangeString(el.TagValue)}\"><color=#ebebeb><u>");
              openTag(el.TagName, "</u></color></link>");
              break;
            }
            else if (rangeIs(el.TagName, "code"))
            {
              if (codeBlocks > 0)
              {
                // TMP doesn't like nested mspace
                res.Append("<line-indent=24px><br>");
                openTag(el.TagName, "</line-indent><br>");
              }
              else
              {
                res.Append("<line-indent=24px><br><mspace=0.75em>");
                openTag(el.TagName, "</mspace></line-indent><br>");
              }
              codeBlocks++;
              break;
            }
            else if (rangeIs(el.TagName, "noparse"))
            {
              noParse = true;
              break;
            }
            appendRange(el.Full);
            break;
          case ElementType.CloseTag:
            if (noParse)
            {
              if (rangeIs(el.TagName, "noparse"))
                noParse = false;
              else
                appendRange(el.Full);
            }
            else if (!closeTag(el.TagName))
              appendRange(el.Full);
            else if (rangeIs(el.TagName, "code"))
              codeBlocks--;
            break;
        }

        Consume(text, ref index);
      }

      // close out anything left open
      while (closeTags.Count > 0)
        res.Append(closeTags.Pop());

      return res.ToString();
    }

    private static bool IsConsumed(char c)
    {
      return c == '\r';
    }

    // consume all characters we aren't going to copy
    private static void Consume(string text, ref int index)
    {
      while (index < text.Length && IsConsumed(text[index])) index++;
    }

    private static Element ParseElement(string text, ref int index)
    {
      var start = index;
      var c = text[index];
      if (c == '\n')
      {
        index++;
        return new Element
        {
          Full = new Range { Start = start, End = index },
          Type = ElementType.Newline,
        };
      }
      if (char.IsWhiteSpace(c))
      {
        while (index < text.Length && text[index] != '\n' && char.IsWhiteSpace(text[index]) && !IsConsumed(text[index]))
        {
          index++;
        }
        return new Element
        {
          Full = new Range { Start = start, End = index },
          Type = ElementType.Whitespace,
        };
      }
      if (c != '[')
      {
        while (index < text.Length && text[index] != '[' && !char.IsWhiteSpace(text[index]) && !IsConsumed(text[index]))
        {
          index++;
        }
        return new Element
        {
          Full = new Range { Start = start, End = index },
          Type = ElementType.Text,
        };
      }
      var tagStart = ++index;
      var isClose = false;
      if (index < text.Length && text[index] == '/')
      {
        isClose = true;
        tagStart = ++index;
      }
      var tagEnd = -1;
      var valueStart = -1;
      while (index < text.Length && text[index] != ']' && text[index] != '\n' && !IsConsumed(text[index]))
      {
        // no starting whitespace
        if (index == tagStart && char.IsWhiteSpace(text[index]))
          break;
        if (text[index] == '=' && valueStart == -1)
          valueStart = index + 1;
        if (tagEnd == -1 && (text[index] == '=' || char.IsWhiteSpace(text[index])))
          tagEnd = index;
        index++;
      }
      if (index == text.Length || text[index] != ']')
      {
        // if we didn't hit ws/equals, send the whole thing as raw text
        if (tagEnd == -1)
          return new Element
          {
            Full = new Range { Start = start, End = index },
            Type = ElementType.Text,
          };
        // otherwise send up to the end of the tag name
        index = tagEnd;
        return new Element
        {
          Full = new Range { Start = start, End = index },
          Type = ElementType.Text,
        };
      }
      // if we found an end, send the tag
      if (tagEnd == -1)
        tagEnd = index;
      if (valueStart == -1)
        valueStart = index;
      var valueEnd = index;
      index++;
      return new Element
      {
        Full = new Range { Start = start, End = index },
        Type = isClose ? ElementType.CloseTag : ElementType.OpenTag,
        TagName = new Range { Start = tagStart, End = tagEnd },
        TagValue = new Range { Start = valueStart, End = valueEnd },
      };
    }

    private struct Range
    {
      public int Start;
      public int End;

      public int Length => this.End - this.Start;
    }

    private enum ElementType
    {
      Whitespace,
      Newline,
      Text,
      OpenTag,
      CloseTag
    }

    private struct Element
    {
      public Range Full;
      public ElementType Type;

      // Only set for OpenTag/CloseTag
      public Range TagName;
      public Range TagValue;
    }
  }
}