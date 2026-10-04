using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ImGuiNET;
using UnityEngine;

namespace StationeersLaunchPad.UI;

// the startup progress bar. countdowns ride the game's rocket, which takes off when they end
public static class RocketBar
{
  private const float Aspect = 256f / 59f;
  private const float NozzleY = 0.2f;

  private static readonly Color FlameOuter = LaunchPadTheme.Hex(0xF47A2A);
  private static readonly Color FlameInner = LaunchPadTheme.Hex(0xFFD35A);
  private static readonly Color Smoke = LaunchPadTheme.Hex(0x3A3D45);
  private static readonly Color Hull = LaunchPadTheme.Hex(0xC9CDD4);

  private const float ClimbSeconds = 1.5f;
  private const float CrashUpSeconds = 0.95f;
  private const float CrashDownSeconds = 0.7f;
  private const float CrashSeconds = 2f;
  private const float CrashChance = 1f / 50f;

  private sealed class Flight
  {
    internal Vector2 Start;
    internal Vector2 Screen;
    internal float Height;
    internal float StartTime;
    internal bool Crash;
  }

  private static readonly List<Flight> flights = [];
  private static Vector2 lastCenter;
  private static float lastHeight;
  private static int lastFrame = -100;
  private static bool alwaysCrash;

  public enum Rocket { None, Pad, Burn, Hold }

  public static float RowHeight => Mathf.Max(ImGui.GetTextLineHeight(), RocketHeight + 2f);
  private static float RocketHeight => Mathf.Round(ImGui.GetTextLineHeight() * 1.1f);

  // draws the bar into the current window, the full content width
  public static void Draw(float fraction, Rocket rocket, bool indeterminate = false)
  {
    var lineHeight = ImGui.GetTextLineHeight();
    var height = RocketHeight;
    var halfLength = height * Aspect / 2f;
    var width = ImGui.GetContentRegionAvail().x;
    var rowHeight = RowHeight;
    var origin = ImGui.GetCursorScreenPos();
    ImGui.Dummy(new Vector2(width, rowHeight));

    var drawList = ImGui.GetWindowDrawList();
    var barHeight = Mathf.Max(3f, Mathf.Round(lineHeight * 0.22f));
    var centerY = origin.y + rowHeight / 2f;
    var min = new Vector2(origin.x, centerY - barHeight / 2f);
    var max = new Vector2(origin.x + width, centerY + barHeight / 2f);
    drawList.AddRectFilled(min, max, LaunchPadTheme.OverU32(Color.white, 0.08f), barHeight / 2f);

    var accent = LaunchPadTheme.Accent;
    if (indeterminate)
    {
      var span = (max.x - min.x) * 0.18f;
      var t = Mathf.Repeat(UnityEngine.Time.unscaledTime * 0.6f, 1f);
      var from = Mathf.Lerp(min.x - span, max.x, t);
      drawList.PushClipRect(min, max, true);
      drawList.AddRectFilled(new Vector2(from, min.y), new Vector2(from + span, max.y), U32(accent), barHeight / 2f);
      drawList.PopClipRect();
      return;
    }

    fraction = Mathf.Clamp01(fraction);
    if (rocket == Rocket.None)
    {
      if (fraction > 0f)
        drawList.AddRectFilled(min, new Vector2(Mathf.Lerp(min.x, max.x, fraction), max.y), U32(accent), barHeight / 2f);
      return;
    }

    // the nose is the head of the bar, the whole rocket stays inside it
    var headX = Mathf.Lerp(min.x + halfLength * 2f, max.x, fraction);
    var center = new Vector2(headX - halfLength, centerY);
    var tailX = center.x - halfLength;
    if (tailX > min.x + 1f)
      drawList.AddRectFilled(min, new Vector2(tailX + height * 0.3f, max.y), U32(accent), barHeight / 2f);

    var time = UnityEngine.Time.unscaledTime;
    if (rocket == Rocket.Burn)
      DrawFlames(drawList, center, 0f, height, 0.9f + Flicker(time) * 0.4f);
    else if (rocket == Rocket.Hold)
      DrawFlames(drawList, center, 0f, height, 0.35f + Flicker(time) * 0.15f);
    DrawRocket(drawList, center, 0f, height);

    lastCenter = center;
    lastHeight = height;
    lastFrame = UnityEngine.Time.frameCount;

    var mouse = ImGui.GetMousePos();
    if (ImGui.IsWindowHovered() && Mathf.Abs(mouse.x - center.x) < halfLength && Mathf.Abs(mouse.y - center.y) < height)
      ImGuiHelper.TextTooltip("Ground crew reminder: don't stand in the exhaust. It's not a bastu.");
  }

  // the rocket drawn last frame lifts off the bar and flies off screen
  public static void Launch()
  {
    if (UnityEngine.Time.frameCount - lastFrame > 5)
      return;
    flights.Add(new Flight
    {
      Start = lastCenter,
      Height = lastHeight,
      Screen = ImGui.GetIO().DisplaySize,
      StartTime = UnityEngine.Time.unscaledTime,
      Crash = alwaysCrash || Random.value < CrashChance,
    });
    lastFrame = -100;
  }

  // secret: the C key on the splash
  public static void AlwaysCrash() => alwaysCrash = true;

  // until the rockets are gone, never longer than a flight
  public static async UniTask WaitForFlights()
  {
    var timeout = UnityEngine.Time.unscaledTime + CrashSeconds + 0.1f;
    while (flights.Count > 0 && UnityEngine.Time.unscaledTime < timeout)
      await UniTask.Yield();
  }

  // call once per frame from the splash, the rockets fly over everything
  public static void DrawFlights()
  {
    if (flights.Count == 0)
      return;
    var drawList = ImGui.GetForegroundDrawList();
    var now = UnityEngine.Time.unscaledTime;
    for (var i = flights.Count - 1; i >= 0; i--)
    {
      var flight = flights[i];
      var t = now - flight.StartTime;
      var done = flight.Crash ? DrawCrash(drawList, flight, t, now) : DrawClimb(drawList, flight, t, now);
      if (done)
        flights.RemoveAt(i);
    }
  }

  // pitch up off the bar and climb straight out of the top of the screen
  private static bool DrawClimb(ImDrawListPtr drawList, Flight flight, float t, float time)
  {
    if (t >= ClimbSeconds)
      return true;
    var h = flight.Height;
    var turn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.25f));
    var u = t / ClimbSeconds;
    var pos = new Vector2(
      flight.Start.x + h * 0.6f * turn,
      flight.Start.y - (flight.Start.y + h * Aspect) * u * u);
    var angle = -Mathf.PI / 2f * turn;
    DrawFlames(drawList, pos, angle, h, 1.6f + Flicker(time) * 0.6f);
    DrawRocket(drawList, pos, angle, h);
    return false;
  }

  // one smooth path: climbs and curls over to the left, nearly stalls at the top,
  // then dives into the left edge of the screen. the nose follows the path
  private static bool DrawCrash(ImDrawListPtr drawList, Flight flight, float t, float time)
  {
    if (t >= CrashSeconds)
      return true;
    var h = flight.Height;
    var half = h * Aspect / 2f;
    var screen = flight.Screen;
    var start = flight.Start;
    var apex = new Vector2(start.x - screen.x * 0.14f, screen.y * 0.1f);
    var wall = new Vector2(half, screen.y * 0.5f);
    Vector2[] up = [start, new(start.x, apex.y + (start.y - apex.y) * 0.35f), new(start.x - screen.x * 0.02f, apex.y), apex];
    Vector2[] down = [apex, apex + new Vector2(-screen.x * 0.18f, 0f), wall + new Vector2(screen.x * 0.2f, -screen.y * 0.12f), wall];

    if (t >= CrashUpSeconds + CrashDownSeconds)
    {
      var end = BezierTangent(down, 1f).normalized;
      DrawBoom(drawList, new Vector2(0f, (wall + end * half).y), h,
        (t - CrashUpSeconds - CrashDownSeconds) / (CrashSeconds - CrashUpSeconds - CrashDownSeconds));
      return false;
    }

    Vector2 pos, dir;
    if (t < CrashUpSeconds)
    {
      // eases in like a normal takeoff and coasts to a stop at the top
      var u = t / CrashUpSeconds;
      var s = AtDistance(up, Mathf.SmoothStep(0f, 1f, u));
      pos = Bezier(up, s);
      dir = BezierTangent(up, s);
    }
    else
    {
      // picks up speed towards the wall
      var v = (t - CrashUpSeconds) / CrashDownSeconds;
      var s = AtDistance(down, Mathf.Pow(v, 1.6f));
      pos = Bezier(down, s);
      dir = BezierTangent(down, s);
    }
    var angle = Mathf.Atan2(dir.y, dir.x);
    // off the bar it still points along it
    if (t < 0.25f)
      angle = Mathf.LerpAngle(0f, angle * Mathf.Rad2Deg, Mathf.SmoothStep(0f, 1f, t / 0.25f)) * Mathf.Deg2Rad;
    DrawFlames(drawList, pos, angle, h, 1.6f + Flicker(time) * 0.6f);
    DrawRocket(drawList, pos, angle, h);
    return false;
  }

  private static Vector2 Bezier(Vector2[] p, float s)
  {
    var r = 1f - s;
    return r * r * r * p[0] + 3f * r * r * s * p[1] + 3f * r * s * s * p[2] + s * s * s * p[3];
  }

  private static Vector2 BezierTangent(Vector2[] p, float s)
  {
    var r = 1f - s;
    return 3f * r * r * (p[1] - p[0]) + 6f * r * s * (p[2] - p[1]) + 3f * s * s * (p[3] - p[2]);
  }

  // the curve parameter at a fraction of the curve's length, so the speed is even along it
  private static float AtDistance(Vector2[] p, float fraction)
  {
    const int steps = 32;
    var lengths = new float[steps + 1];
    var prev = p[0];
    for (var i = 1; i <= steps; i++)
    {
      var point = Bezier(p, i / (float)steps);
      lengths[i] = lengths[i - 1] + (point - prev).magnitude;
      prev = point;
    }
    var target = Mathf.Clamp01(fraction) * lengths[steps];
    for (var i = 1; i <= steps; i++)
      if (lengths[i] >= target)
        return (i - 1 + Mathf.InverseLerp(lengths[i - 1], lengths[i], target)) / steps;
    return 1f;
  }

  // opaque only, translucent fills turn pink over the splash
  private static void DrawBoom(ImDrawListPtr drawList, Vector2 at, float h, float e)
  {
    var color = e < 0.3f ? Color.Lerp(FlameInner, FlameOuter, e / 0.3f) : Color.Lerp(FlameOuter, Smoke, (e - 0.3f) / 0.7f);
    var size = h * (1.5f + 5f * e) * (1f - e * e * e);
    for (var k = 0; k < 6; k++)
    {
      var a = (k / 6f - 0.5f) * Mathf.PI;
      var offset = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * size * 0.7f;
      drawList.AddCircleFilled(at + offset, size * (0.55f + 0.1f * (k % 3)), U32(color), 16);
    }
    drawList.AddCircleFilled(at, size * 0.6f, U32(Color.Lerp(FlameInner, color, e)), 16);

    // bits of rocket bouncing off the wall
    var hull = U32(Hull);
    var s = e * (CrashSeconds - CrashUpSeconds - CrashDownSeconds);
    for (var k = 0; k < 8; k++)
    {
      var a = (k / 7f - 0.5f) * Mathf.PI * 0.8f;
      var speed = h * (12f + 8f * Mathf.Abs(Mathf.Sin(k * 12.9898f)));
      var pos = at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed * s + new Vector2(0f, h * 40f * s * s);
      var spin = s * 12f + k;
      var r = new Vector2(Mathf.Cos(spin), Mathf.Sin(spin)) * h * 0.18f;
      var n = new Vector2(-r.y, r.x) * 0.6f;
      drawList.AddQuadFilled(pos - r - n, pos + r - n, pos + r + n, pos - r + n, hull);
    }
  }

  private static float Flicker(float time) => Mathf.PerlinNoise(time * 14f, 0.37f);

  // local coords are in rocket heights, x along the rocket
  private static Vector2 Local(Vector2 center, float angle, float h, float x, float y)
  {
    var cos = Mathf.Cos(angle);
    var sin = Mathf.Sin(angle);
    return center + new Vector2(x * cos - y * sin, x * sin + y * cos) * h;
  }

  private static void DrawRocket(ImDrawListPtr drawList, Vector2 center, float angle, float h)
  {
    if (!ModImages.TryGetBuiltIn(ModImages.RocketImage, out var id, out _))
      return;
    var half = Aspect / 2f;
    drawList.AddImageQuad(id,
      Local(center, angle, h, -half, -0.5f), Local(center, angle, h, half, -0.5f),
      Local(center, angle, h, half, 0.5f), Local(center, angle, h, -half, 0.5f),
      new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
  }

  // one flame per nozzle
  private static void DrawFlames(ImDrawListPtr drawList, Vector2 center, float angle, float h, float length)
  {
    var tail = -Aspect / 2f + 0.02f;
    foreach (var y in new[] { -NozzleY, NozzleY })
    {
      drawList.AddTriangleFilled(Local(center, angle, h, tail, y - 0.17f), Local(center, angle, h, tail - length, y),
        Local(center, angle, h, tail, y + 0.17f), U32(FlameOuter));
      drawList.AddTriangleFilled(Local(center, angle, h, tail, y - 0.09f), Local(center, angle, h, tail - length * 0.55f, y),
        Local(center, angle, h, tail, y + 0.09f), U32(FlameInner));
    }
  }

  private static uint U32(Color color) => ImGui.ColorConvertFloat4ToU32((Vector4)color);
}
