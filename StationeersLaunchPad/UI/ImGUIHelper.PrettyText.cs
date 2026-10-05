using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using ImGuiNET;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public static partial class ImGuiHelper
{
  /// <summary>
  /// Renders workshop-style markup ([b], [i], [u], [strike], [h1-3], [url], [code], [list], [quote], [hr], [noparse]).
  /// [img] and videos are dropped, unknown tags are shown as-is.
  /// </summary>
  public static void TextPretty(string text)
  {
    if (string.IsNullOrEmpty(text)) return;

    var tokens = GetTokens(text);
    var stack = new Stack<RenderState>();
    var lists = new Stack<int>(); // -1 = bullet list, otherwise next ordered number
    var state = RenderState.Default();
    var line = new LineState
    {
      StartX = ImGui.GetCursorScreenPos().x,
      Right = ImGui.GetCursorScreenPos().x + Math.Max(50f, ImGui.GetContentRegionAvail().x),
      AtStart = true,
    };
    var indentStep = ImGui.CalcTextSize("    ").x;

    ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.x, 2f));

    foreach (var token in tokens)
    {
      switch (token.Kind)
      {
        case TokenKind.Text:
          RenderWrapped(token.Value, state, ref line);
          break;

        case TokenKind.Newline:
          if (line.AtStart)
          {
            // blank line: paragraph gap, but never a full empty line
            if (!line.LastWasGap)
              ImGui.Dummy(new Vector2(0f, ImGui.GetTextLineHeight() * 0.5f));
            line.LastWasGap = true;
          }
          line.AtStart = true;
          line.Indent = lists.Count * indentStep + state.QuoteIndent;
          break;

        case TokenKind.Bullet:
          EndLine(ref line);
          line.Indent = Math.Max(0, lists.Count - 1) * indentStep + state.QuoteIndent;
          var marker = "•";
          if (lists.Count > 0 && lists.Peek() >= 0)
          {
            var number = lists.Pop();
            marker = $"{number}.";
            lists.Push(number + 1);
          }
          else if (!HasGlyph('•'))
            marker = "-";
          RenderWrapped(marker + " ", state with { Color = (Vector4)LaunchPadTheme.TextMuted }, ref line);
          // wrapped lines of this item hang under its text, not under the marker
          line.Indent = ImGui.GetItemRectMax().x - line.StartX;
          break;

        case TokenKind.Rule:
          EndLine(ref line);
          ImGui.Dummy(new Vector2(0f, 2f));
          ImGui.Separator();
          ImGui.Dummy(new Vector2(0f, 2f));
          line.LastWasGap = true;
          break;

        case TokenKind.OpenTag:
          if (token.Tag is "list" or "olist")
          {
            EndLine(ref line);
            lists.Push(token.Tag == "olist" ? 1 : -1);
            break;
          }
          if (token.Tag == "quote")
          {
            EndLine(ref line);
            stack.Push(state);
            state = state with
            {
              Color = (Vector4)LaunchPadTheme.TextSub,
              QuoteIndent = state.QuoteIndent + indentStep,
            };
            line.Indent = lists.Count * indentStep + state.QuoteIndent;
            break;
          }
          if (TagOpeners.TryGetValue(token.Tag, out var opener))
          {
            stack.Push(state);
            state = opener(state, token.Value);
          }
          break;

        case TokenKind.CloseTag:
          if (token.Tag is "list" or "olist")
          {
            if (lists.Count > 0)
              lists.Pop();
            EndLine(ref line);
            line.Indent = lists.Count * indentStep + state.QuoteIndent;
            break;
          }
          if (stack.Count > 0)
            state = stack.Pop();
          if (token.Tag == "quote")
          {
            EndLine(ref line);
            line.Indent = lists.Count * indentStep + state.QuoteIndent;
          }
          break;
      }
    }

    ImGui.PopStyleVar();
  }

  private enum TokenKind { Text, Newline, OpenTag, CloseTag, Bullet, Rule }

  private readonly struct Token
  {
    public readonly TokenKind Kind;
    public readonly string Tag;
    public readonly string Value;

    public Token(TokenKind kind, string value = null, string tag = null)
    {
      Kind = kind;
      Value = value;
      Tag = tag;
    }
  }

  private struct LineState
  {
    public float StartX;
    public float Right;
    public float Indent;
    public bool AtStart;
    public bool LastWasGap;
  }

  private static void EndLine(ref LineState line)
  {
    line.AtStart = true;
  }

  // tags that change how text looks, StructuralTags are handled in TextPretty
  private static readonly HashSet<string> KnownTags = new(StringComparer.Ordinal)
  {
    "b", "i", "u", "strike", "s", "h1", "h2", "h3", "url", "code", "spoiler",
    "list", "olist", "quote", "red", "green", "blue", "yellow", "cyan",
  };

  // image/video urls
  private static readonly HashSet<string> DroppedTags = new(StringComparer.Ordinal)
  {
    "img", "previewyoutube", "previewvideo",
  };

  // tables can't be rendered here, keep the text
  private static readonly HashSet<string> IgnoredTags = new(StringComparer.Ordinal)
  {
    "table", "tr", "td", "th", "noparse",
  };

  private static readonly Dictionary<string, List<Token>> tokenCache = new();
  private static readonly Regex BlankLines = new(@"\n{3,}", RegexOptions.Compiled);

  // the same few texts are drawn every frame
  private static List<Token> GetTokens(string text)
  {
    if (tokenCache.TryGetValue(text, out var cached))
      return cached;
    if (tokenCache.Count > 128)
      tokenCache.Clear();
    var tokens = Lex(Normalize(text));
    tokenCache[text] = tokens;
    return tokens;
  }

  // About.xml text is indented like the XML, and the game font has no emoji
  private static string Normalize(string text)
  {
    var lines = text.Replace("\r", "").Split('\n');
    var sb = new StringBuilder(text.Length);
    for (var i = 0; i < lines.Length; i++)
    {
      if (i > 0)
        sb.Append('\n');
      foreach (var c in lines[i].Trim())
      {
        if (c == '\t')
          sb.Append(' ');
        else if (HasGlyph(c))
          sb.Append(c);
      }
    }
    return BlankLines.Replace(sb.ToString(), "\n\n").Trim('\n');
  }

  internal static unsafe bool HasGlyph(char c)
  {
    if (c < 0x80)
      return true;
    if (char.IsSurrogate(c))
      return false;
    return ImGui.GetFont().FindGlyphNoFallback(c).NativePtr != null;
  }

  private static List<Token> Lex(string text)
  {
    var tokens = new List<Token>();
    var pos = 0;
    var textStart = 0;

    void FlushText(int end)
    {
      if (end > textStart)
        tokens.Add(new Token(TokenKind.Text, value: text[textStart..end]));
    }

    while (pos < text.Length)
    {
      var c = text[pos];
      if (c == '\n')
      {
        FlushText(pos);
        tokens.Add(new Token(TokenKind.Newline));
        textStart = ++pos;
        continue;
      }
      if (c != '[')
      {
        pos++;
        continue;
      }

      var end = text.IndexOf(']', pos + 1);
      var lineEnd = text.IndexOf('\n', pos + 1);
      if (end == -1 || (lineEnd != -1 && lineEnd < end))
      {
        pos++;
        continue;
      }

      var inner = text.Substring(pos + 1, end - pos - 1);
      var closing = inner.StartsWith("/");
      var body = closing ? inner.Substring(1) : inner;
      var eq = body.IndexOf('=');
      var tag = (eq >= 0 ? body.Substring(0, eq) : body).Trim().ToLowerInvariant();
      var attr = eq >= 0 ? body.Substring(eq + 1).Trim().Trim('"', '\'') : null;

      if (tag == "*" && !closing)
      {
        FlushText(pos);
        tokens.Add(new Token(TokenKind.Bullet));
        textStart = pos = end + 1;
        continue;
      }
      if (tag == "hr")
      {
        FlushText(pos);
        if (!closing)
          tokens.Add(new Token(TokenKind.Rule));
        textStart = pos = end + 1;
        continue;
      }
      if (tag == "noparse" && !closing)
      {
        // everything up to [/noparse] is literal text
        FlushText(pos);
        var close = text.IndexOf("[/noparse]", end + 1, StringComparison.OrdinalIgnoreCase);
        var literalEnd = close == -1 ? text.Length : close;
        if (literalEnd > end + 1)
          tokens.Add(new Token(TokenKind.Text, value: text[(end + 1)..literalEnd]));
        textStart = pos = close == -1 ? text.Length : close + "[/noparse]".Length;
        continue;
      }
      if (DroppedTags.Contains(tag))
      {
        FlushText(pos);
        var closeTag = $"[/{tag}]";
        var close = closing ? -1 : text.IndexOf(closeTag, end + 1, StringComparison.OrdinalIgnoreCase);
        textStart = pos = close == -1 ? end + 1 : close + closeTag.Length;
        continue;
      }
      if (IgnoredTags.Contains(tag))
      {
        FlushText(pos);
        textStart = pos = end + 1;
        continue;
      }
      if (KnownTags.Contains(tag))
      {
        FlushText(pos);
        tokens.Add(new Token(closing ? TokenKind.CloseTag : TokenKind.OpenTag, tag: tag, value: attr));
        textStart = pos = end + 1;
        continue;
      }
      pos = end + 1;
    }
    FlushText(text.Length);
    return tokens;
  }

  private record struct RenderState
  {
    public float Scale;
    public Vector4 Color;
    public bool Bold;
    public bool Underline;
    public bool Strike;
    public string Link;
    public bool LinkIsText;
    public float QuoteIndent;

    public static RenderState Default() => new()
    {
      Scale = 1.0f,
      Color = ImGui.GetStyle().Colors[(int)ImGuiCol.Text],
    };
  }

  private static readonly Vector4 ColorLink = new(0.40f, 0.70f, 1.00f, 1.0f);
  private static readonly Vector4 ColorHeading = new(0.35f, 0.66f, 0.84f, 1.0f);
  private static readonly Vector4 ColorCode = new(0.60f, 0.60f, 0.60f, 1.0f);
  private static readonly Vector4 ColorRed = new(1.00f, 0.25f, 0.25f, 1.0f);
  private static readonly Vector4 ColorGreen = new(0.20f, 0.90f, 0.40f, 1.0f);
  private static readonly Vector4 ColorBlue = new(0.30f, 0.60f, 1.00f, 1.0f);
  private static readonly Vector4 ColorYellow = new(1.00f, 0.90f, 0.20f, 1.0f);
  private static readonly Vector4 ColorCyan = new(0.20f, 0.95f, 0.95f, 1.0f);

  // the game font has no bold/italic faces, bold is drawn twice with a 1px offset and italic is dimmed
  private static readonly Dictionary<string, Func<RenderState, string, RenderState>> TagOpeners =
    new(StringComparer.Ordinal)
    {
      ["b"] = (s, _) => s with { Bold = true },
      ["i"] = (s, _) => s with { Color = Vector4.Scale(s.Color, new Vector4(0.8f, 0.8f, 0.8f, 1f)) },
      ["u"] = (s, _) => s with { Underline = true },
      ["strike"] = (s, _) => s with { Strike = true },
      ["s"] = (s, _) => s with { Strike = true },
      ["spoiler"] = (s, _) => s with { Color = (Vector4)LaunchPadTheme.TextMuted },
      ["h1"] = (s, _) => s with { Scale = 1.4f, Color = ColorHeading, Bold = true },
      ["h2"] = (s, _) => s with { Scale = 1.25f, Color = ColorHeading, Bold = true },
      ["h3"] = (s, _) => s with { Scale = 1.1f, Color = ColorHeading, Bold = true },
      ["code"] = (s, _) => s with { Color = ColorCode },
      ["url"] = (s, attr) => s with
      {
        Link = string.IsNullOrEmpty(attr) ? null : attr,
        LinkIsText = string.IsNullOrEmpty(attr),
        Color = ColorLink,
        Underline = true,
      },
      ["red"] = (s, _) => s with { Color = ColorRed },
      ["green"] = (s, _) => s with { Color = ColorGreen },
      ["blue"] = (s, _) => s with { Color = ColorBlue },
      ["yellow"] = (s, _) => s with { Color = ColorYellow },
      ["cyan"] = (s, _) => s with { Color = ColorCyan },
    };

  // lays out as many words as fit on a line as one item
  private static void RenderWrapped(string text, RenderState state, ref LineState line)
  {
    var spaceWidth = ImGui.CalcTextSize(" ").x * state.Scale;
    var run = new StringBuilder();
    var runWidth = 0f;
    var x = line.AtStart ? line.StartX + line.Indent : ImGui.GetItemRectMax().x;

    var i = 0;
    while (i < text.Length)
    {
      // next word plus the spaces before it
      var spaces = 0;
      while (i < text.Length && text[i] == ' ')
      {
        spaces++;
        i++;
      }
      var wordStart = i;
      while (i < text.Length && text[i] != ' ')
        i++;
      var word = text[wordStart..i];
      if (word.Length == 0)
      {
        // trailing spaces only matter mid-line (e.g. before a [b] tag)
        if (!line.AtStart || run.Length > 0)
        {
          run.Append(' ', spaces);
          runWidth += spaces * spaceWidth;
        }
        break;
      }

      var lead = line.AtStart && run.Length == 0 ? 0 : spaces;
      var wordWidth = ImGui.CalcTextSize(word).x * state.Scale;
      var fits = x + runWidth + lead * spaceWidth + wordWidth <= line.Right;
      if (!fits && (run.Length > 0 || !line.AtStart))
      {
        FlushRun(run.ToString(), state, ref line);
        line.AtStart = true;
        run.Clear();
        runWidth = 0f;
        x = line.StartX + line.Indent;
        lead = 0;
      }
      if (state.Link == null && !state.LinkIsText
        && (word.StartsWith("http://") || word.StartsWith("https://")))
      {
        // bare urls become links on their own, trailing punctuation stays plain text
        run.Append(' ', lead);
        if (run.Length > 0)
          FlushRun(run.ToString(), state, ref line);
        run.Clear();
        runWidth = 0f;
        var url = word.TrimEnd('.', ',', ')', ';', ':', '!', '?');
        FlushRun(url, state with { Link = url, Color = ColorLink, Underline = true }, ref line);
        x = ImGui.GetItemRectMax().x;
        if (url.Length < word.Length)
        {
          run.Append(word, url.Length, word.Length - url.Length);
          runWidth = ImGui.CalcTextSize(word[url.Length..]).x * state.Scale;
        }
        continue;
      }

      run.Append(' ', lead).Append(word);
      runWidth += lead * spaceWidth + wordWidth;
    }

    if (run.Length > 0)
      FlushRun(run.ToString(), state, ref line);
  }

  private static void FlushRun(string run, RenderState state, ref LineState line)
  {
    if (run.Length == 0)
      return;

    if (line.AtStart)
      ImGui.SetCursorScreenPos(new Vector2(line.StartX + line.Indent, ImGui.GetCursorScreenPos().y));
    else
      ImGui.SameLine(0, 0);

    ImGui.PushStyleColor(ImGuiCol.Text, state.Color);
    ImGui.SetWindowFontScale(state.Scale);
    ImGui.TextUnformatted(run);
    var min = ImGui.GetItemRectMin();
    var max = ImGui.GetItemRectMax();
    var drawList = ImGui.GetWindowDrawList();
    var col = ImGui.GetColorU32(state.Color);
    if (state.Bold)
      drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), min + new Vector2(1f, 0f), col, run);
    ImGui.SetWindowFontScale(1.0f);
    ImGui.PopStyleColor();

    if (state.Underline)
      drawList.AddLine(new Vector2(min.x, max.y), new Vector2(max.x, max.y), col, 1.0f);
    if (state.Strike)
    {
      var midY = (min.y + max.y) * 0.5f;
      drawList.AddLine(new Vector2(min.x, midY), new Vector2(max.x, midY), col, 1.0f);
    }

    var link = state.LinkIsText ? run.Trim() : state.Link;
    if (!string.IsNullOrEmpty(link) && ImGui.IsItemHovered())
    {
      ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
      TextTooltip(link);
      if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        Application.OpenURL(link);
    }

    line.AtStart = false;
    line.LastWasGap = false;
  }
}
