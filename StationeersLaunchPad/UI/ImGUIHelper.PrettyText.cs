using System;
using System.Collections.Generic;
using ImGuiNET;
using UnityEngine;

namespace StationeersLaunchPad.UI;

public static partial class ImGuiHelper
{
  /// <summary>
  /// Renders text with workshop-style formatting ([b], [i], [u], [strike], [h1-3], [url=…], [code], [list]/[*]).
  /// Supports nested tags via a state stack. Links open on click.
  /// Unknown tags are passed through as raw text.
  /// </summary>
  public static void TextPretty(string text)
  {
    if (string.IsNullOrEmpty(text)) return;

    var tokens        = Lex(text);
    var stack         = new Stack<RenderState>();
    var current       = RenderState.Default();
    bool needSameLine = false;

    // Tighter line spacing for news/richtext than default ItemSpacing.y (adjust the 2f here if needed)
    ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.x, 2f));

    foreach (var token in tokens)
    {
      switch (token.Kind)
      {
        case TokenKind.OpenTag:
          if (TagOpeners.TryGetValue(token.Tag, out var opener))
          {
            stack.Push(current);
            current = opener(current, token.Value);
          }
          break;

        case TokenKind.CloseTag:
          if (stack.Count > 0)
            current = stack.Pop();
          break;

        case TokenKind.Text:
          RenderChunk(token.Value, current, ref needSameLine);
          break;

        case TokenKind.Newline:
          if (needSameLine)
            needSameLine = false;
          else
            ImGui.NewLine();
          break;

        case TokenKind.BulletItem:
          if (needSameLine)
            needSameLine = false;
          ImGui.Bullet();
          ImGui.SameLine(0, 4);
          RenderChunk(token.Value, current, ref needSameLine);
          needSameLine = false;
          break;
      }
    }

    ImGui.PopStyleVar();
  }

  private enum TokenKind { Text, Newline, OpenTag, CloseTag, BulletItem }

  private readonly struct Token
  {
    public readonly TokenKind Kind;
    public readonly string    Tag;
    public readonly string    Value;

    public Token(TokenKind kind, string value = null, string tag = null)
    {
      Kind  = kind;
      Value = value;
      Tag   = tag;
    }
  }

  private static readonly HashSet<string> KnownTags =
    new(StringComparer.Ordinal)
    { "b", "i", "u", "strike", "h1", "h2", "h3", "url", "code", "list", "red", "green", "blue", "yellow", "cyan" };

  private static List<Token> Lex(string text)
  {
    var tokens = new List<Token>();
    int pos    = 0;

    while (pos < text.Length)
    {
      if (text[pos] == '\n')
      {
        tokens.Add(new Token(TokenKind.Newline));
        pos++;
        continue;
      }

      if (text[pos] == '[')
      {
        int end = text.IndexOf(']', pos + 1);
        if (end != -1)
        {
          string inner   = text.Substring(pos + 1, end - pos - 1);
          bool   closing = inner.StartsWith("/");
          string body    = closing ? inner.Substring(1) : inner;
          int    eq      = body.IndexOf('=');
          string tagName = eq >= 0 ? body.Substring(0, eq) : body;
          string attr    = eq >= 0 ? body.Substring(eq + 1) : null;

          if (tagName == "*" && !closing)
          {
            int lineEnd = text.IndexOf('\n', end + 1);
            string itemText = lineEnd >= 0
              ? text.Substring(end + 1, lineEnd - end - 1).Trim()
              : text[(end + 1)..].Trim();
            tokens.Add(new Token(TokenKind.BulletItem, value: itemText));
            pos = lineEnd >= 0 ? lineEnd : text.Length;
            continue;
          }

          if (KnownTags.Contains(tagName))
          {
            tokens.Add(new Token(
              closing ? TokenKind.CloseTag : TokenKind.OpenTag,
              tag:   tagName,
              value: attr));
            pos = end + 1;
            continue;
          }
        }
      }

      int next = pos;
      while (next < text.Length && text[next] != '[' && text[next] != '\n') next++;
      if (next > pos)
        tokens.Add(new Token(TokenKind.Text, value: text[pos..next]));
      pos = next;
    }

    return tokens;
  }

  private struct RenderState
  {
    public float   Scale;
    public Vector4 Color;
    public bool    Underline;
    public bool    Strike;
    public string  Link;

    public static RenderState Default() => new RenderState
    {
      Scale     = 1.0f,
      Color     = ImGui.GetStyle().Colors[(int)ImGuiCol.Text],
      Underline = false,
      Strike    = false,
      Link      = null,
    };
  }

  private static readonly Vector4 ColorLink    = new Vector4(0.40f, 0.70f, 1.00f, 1.0f);
  private static readonly Vector4 ColorHeading = new Vector4(0.35f, 0.66f, 0.84f, 1.0f);
  private static readonly Vector4 ColorCode    = new Vector4(0.60f, 0.60f, 0.60f, 1.0f);
  private static readonly Vector4 ColorRed     = new Vector4(1.00f, 0.25f, 0.25f, 1.0f);
  private static readonly Vector4 ColorGreen   = new Vector4(0.20f, 0.90f, 0.40f, 1.0f);
  private static readonly Vector4 ColorBlue    = new Vector4(0.30f, 0.60f, 1.00f, 1.0f);
  private static readonly Vector4 ColorYellow  = new Vector4(1.00f, 0.90f, 0.20f, 1.0f);
  private static readonly Vector4 ColorCyan    = new Vector4(0.20f, 0.95f, 0.95f, 1.0f);

  private static readonly Dictionary<string, Func<RenderState, string, RenderState>> TagOpeners =
    new Dictionary<string, Func<RenderState, string, RenderState>>(StringComparer.Ordinal)
    {
      ["b"]      = (s, _) => s with { Scale = 1.15f },
      ["i"]      = (s, _) => s with { Scale = 0.90f },
      ["u"]      = (s, _) => s with { Underline = true },
      ["strike"] = (s, _) => s with { Strike = true },
      ["h1"]     = (s, _) => s with { Scale = 1.66f, Color = ColorHeading },
      ["h2"]     = (s, _) => s with { Scale = 1.50f, Color = ColorHeading },
      ["h3"]     = (s, _) => s with { Scale = 1.33f, Color = ColorHeading },
      ["code"]   = (s, _) => s with { Scale = 0.85f, Color = ColorCode },
      ["url"]    = (s, attr) => s with
      {
        Link      = string.IsNullOrEmpty(attr) ? null : attr,
        Color     = ColorLink,
        Underline = true,
      },
      ["red"]    = (s, _) => s with { Color = ColorRed },
      ["green"]  = (s, _) => s with { Color = ColorGreen },
      ["blue"]   = (s, _) => s with { Color = ColorBlue },
      ["yellow"] = (s, _) => s with { Color = ColorYellow },
      ["cyan"]   = (s, _) => s with { Color = ColorCyan },
    };
  
  private static void RenderChunk(string chunk, RenderState state, ref bool needSameLine)
  {
    if (string.IsNullOrEmpty(chunk)) return;

    if (needSameLine) ImGui.SameLine(0, 0);

    ImGui.PushStyleColor(ImGuiCol.Text, state.Color);
    ImGui.SetWindowFontScale(state.Scale);
    ImGui.TextUnformatted(chunk);
    ImGui.SetWindowFontScale(1.0f);
    ImGui.PopStyleColor();

    if (state.Underline || state.Strike)
    {
      var  min = ImGui.GetItemRectMin();
      var  max = ImGui.GetItemRectMax();
      var  dl  = ImGui.GetWindowDrawList();
      uint col = ImGui.GetColorU32(state.Color);

      if (state.Underline)
        dl.AddLine(new Vector2(min.x, max.y), new Vector2(max.x, max.y), col, 1.0f);

      if (state.Strike)
      {
        float midY = (min.y + max.y) * 0.5f;
        dl.AddLine(new Vector2(min.x, midY), new Vector2(max.x, midY), col, 1.0f);
      }
    }

    if (state.Link != null && ImGui.IsItemHovered())
    {
      TextTooltip(state.Link);
      if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        Application.OpenURL(state.Link);
    }

    needSameLine = true;
  }
}
