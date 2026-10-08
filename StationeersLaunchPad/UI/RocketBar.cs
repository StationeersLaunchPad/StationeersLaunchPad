using System.Collections.Generic;
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
  private const float CrashSeconds = CrashUpSeconds + CrashDownSeconds + BoomSeconds;
  private const float CrashChance = 1f / 50f;
  private const float DiveOutSeconds = 0.6f;
  private const float DiveInSeconds = 1.5f;
  private const float BoomSeconds = 0.35f;

  private enum Path { Climb, Crash, Dive }

  private sealed class Flight
  {
    internal Vector2 Start;
    internal float Height;
    internal float StartTime;
    internal Path Path;
    internal System.Action OnExit;
  }

  private static readonly List<Flight> flights = [];
  private static Vector2 lastCenter;
  private static float lastHeight;
  private static int lastFrame = -100;
  private static bool alwaysCrash;
  private static float shown;

  // the Load Mods button, where the rocket ends up when it dives into the SLP menu
  public static Vector2? DiveTarget;

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
    if (flights.Count > 0)
      rocket = Rocket.None;
    if (rocket == Rocket.None)
    {
      if (fraction > 0f)
        drawList.AddRectFilled(min, new Vector2(Mathf.Lerp(min.x, max.x, fraction), max.y), U32(accent), barHeight / 2f);
      return;
    }

    // glides when the countdown jumps, starts where it is on a new bar
    shown = UnityEngine.Time.frameCount - lastFrame > 5 ? fraction
      : Mathf.Lerp(shown, fraction, 1f - Mathf.Exp(-UnityEngine.Time.unscaledDeltaTime * 14f));
    // the nose is the head of the bar, the whole rocket stays inside it
    var headX = Mathf.Lerp(min.x + halfLength * 2f, max.x, shown);
    var center = new Vector2(headX - halfLength, centerY);
    var tailX = center.x - halfLength;
    if (tailX > min.x + 1f)
      drawList.AddRectFilled(min, new Vector2(tailX + height * 0.3f, max.y), U32(accent), barHeight / 2f);

    var flicker = Flicker(UnityEngine.Time.unscaledTime);
    DrawShip(drawList, center, 0f, height, rocket switch
    {
      Rocket.Burn => 0.9f + flicker * 0.4f,
      Rocket.Hold => 0.35f + flicker * 0.15f,
      _ => 0f,
    });

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
    if (!Configs.DevMode.Value)
      Fly(alwaysCrash || Random.value < CrashChance ? Path.Crash : Path.Climb);
  }

  // the rocket drawn last frame dives off the splash, onExit opens the SLP menu, then it hits Load Mods
  public static void Dive(System.Action onExit)
  {
    if (!Fly(Path.Dive, onExit))
      onExit();
  }

  // the rocket is on its way off the splash
  public static bool Diving => flights.Exists(flight => flight.OnExit != null);

  // the longest takeoff, a countdown launches this long before it ends
  public const float TakeoffSeconds = CrashSeconds;

  private static bool Fly(Path path, System.Action onExit = null)
  {
    if (UnityEngine.Time.frameCount - lastFrame > 5)
      return false;
    flights.Add(new Flight
    {
      Start = lastCenter,
      Height = lastHeight,
      StartTime = UnityEngine.Time.unscaledTime,
      Path = path,
      OnExit = onExit,
    });
    lastFrame = -100;
    return true;
  }

  // secret: the C key on the splash
  public static void AlwaysCrash() => alwaysCrash = true;

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
      var done = flight.Path switch
      {
        Path.Crash => DrawCrash(drawList, flight, t, now),
        Path.Dive => DrawDive(drawList, flight, t, now),
        _ => DrawClimb(drawList, flight, t, now),
      };
      if (done)
        flights.RemoveAt(i);
    }
  }

  // straight out of the top of the screen
  private static bool DrawClimb(ImDrawListPtr drawList, Flight flight, float t, float time)
  {
    if (t >= ClimbSeconds)
      return true;
    var (pos, angle) = LiftOff(flight, t / ClimbSeconds, -1f, flight.Start.y + flight.Height * Aspect, 2f, t);
    DrawShip(drawList, pos, angle, flight.Height, 1.6f + Flicker(time) * 0.6f);
    return false;
  }

  // turns off the bar towards straight up (-1) or down (1) and covers the distance by u = 1
  private static (Vector2 pos, float angle) LiftOff(Flight flight, float u, float vertical, float distance, float power, float t)
  {
    var turn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.25f));
    var pos = flight.Start + new Vector2(flight.Height * 0.6f * turn, vertical * distance * Mathf.Pow(u, power));
    return (pos, vertical * Mathf.PI / 2f * turn);
  }

  // one smooth path: climbs and curls over to the left, nearly stalls at the top,
  // then dives into the left edge of the screen. the nose follows the path
  private static bool DrawCrash(ImDrawListPtr drawList, Flight flight, float t, float time)
  {
    if (t >= CrashSeconds)
      return true;
    var h = flight.Height;
    var half = h * Aspect / 2f;
    var screen = ImGui.GetIO().DisplaySize;
    var start = flight.Start;
    var apex = new Vector2(start.x - screen.x * 0.14f, screen.y * 0.1f);
    var wall = new Vector2(half, screen.y * 0.5f);
    Vector2[] up = [start, new(start.x, apex.y + (start.y - apex.y) * 0.35f), new(start.x - screen.x * 0.02f, apex.y), apex];
    Vector2[] down = [apex, apex + new Vector2(-screen.x * 0.18f, 0f), wall + new Vector2(screen.x * 0.2f, -screen.y * 0.12f), wall];

    if (t >= CrashUpSeconds + CrashDownSeconds)
    {
      var end = BezierTangent(down, 1f).normalized;
      DrawBoom(drawList, new Vector2(0f, (wall + end * half).y), 0f, h, (t - CrashUpSeconds - CrashDownSeconds) / BoomSeconds);
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
      var v = (t - CrashUpSeconds) / CrashDownSeconds;
      var s = AtDistance(down, Mathf.Pow(v, 1.6f));
      pos = Bezier(down, s);
      dir = BezierTangent(down, s);
    }
    var angle = Mathf.Atan2(dir.y, dir.x);
    // turns off the bar instead of snapping to the path
    if (t < 0.25f)
      angle = Mathf.LerpAngle(0f, angle * Mathf.Rad2Deg, Mathf.SmoothStep(0f, 1f, t / 0.25f)) * Mathf.Deg2Rad;
    DrawShip(drawList, pos, angle, h, 1.6f + Flicker(time) * 0.6f);
    return false;
  }

  // noses down off the splash and swoops across the SLP menu into Load Mods
  private static bool DrawDive(ImDrawListPtr drawList, Flight flight, float t, float time)
  {
    if (t >= DiveOutSeconds + DiveInSeconds + BoomSeconds)
      return true;
    var h = flight.Height;
    var half = h * Aspect / 2f;
    var screen = ImGui.GetIO().DisplaySize;
    var target = DiveTarget ?? new Vector2(screen.x * 0.85f, screen.y * 0.9f);

    if (t >= DiveOutSeconds + DiveInSeconds)
    {
      DrawBoom(drawList, target, -Mathf.PI / 2f, h, (t - DiveOutSeconds - DiveInSeconds) / BoomSeconds);
      return false;
    }

    Vector2 pos;
    float angle;
    if (t < DiveOutSeconds)
      (pos, angle) = LiftOff(flight, t / DiveOutSeconds, 1f, screen.y - flight.Start.y + half * 2f, 1.4f, t);
    else
    {
      // off the bottom: the menu opens, the rocket comes back in at the top in the same column
      flight.OnExit?.Invoke();
      flight.OnExit = null;
      var entry = new Vector2(flight.Start.x + h * 0.6f, -half * 2f);
      Vector2[] path = [entry, entry + new Vector2(0f, screen.y * 0.45f), target + new Vector2(-screen.x * 0.35f, -screen.y * 0.05f), target];
      var s = AtDistance(path, Glide((t - DiveOutSeconds) / DiveInSeconds));
      pos = Bezier(path, s);
      var dir = BezierTangent(path, s);
      angle = Mathf.Atan2(dir.y, dir.x);
    }
    DrawShip(drawList, pos, angle, h, 1.6f + Flicker(time) * 0.6f);
    return false;
  }

  // 0 to 1 at twice the average speed at both ends, half of it in the middle
  private static float Glide(float u) => u * (2f * u * u - 3f * u + 2f);

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
  // facing is where the debris flies, away from what the rocket hit
  private static void DrawBoom(ImDrawListPtr drawList, Vector2 at, float facing, float h, float e)
  {
    var color = e < 0.3f ? Color.Lerp(FlameInner, FlameOuter, e / 0.3f) : Color.Lerp(FlameOuter, Smoke, (e - 0.3f) / 0.7f);
    var size = h * (1.5f + 5f * e) * (1f - e * e * e);
    for (var k = 0; k < 6; k++)
    {
      var a = facing + (k / 6f - 0.5f) * Mathf.PI;
      var offset = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * size * 0.7f;
      drawList.AddCircleFilled(at + offset, size * (0.55f + 0.1f * (k % 3)), U32(color), 16);
    }
    drawList.AddCircleFilled(at, size * 0.6f, U32(Color.Lerp(FlameInner, color, e)), 16);

    // bits of rocket bouncing off
    var hull = U32(Hull);
    var s = e * BoomSeconds;
    for (var k = 0; k < 8; k++)
    {
      var a = facing + (k / 7f - 0.5f) * Mathf.PI * 0.8f;
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

  // the sprite with a flame per nozzle, flame is its length in rocket heights
  private static void DrawShip(ImDrawListPtr drawList, Vector2 center, float angle, float h, float flame)
  {
    var half = Aspect / 2f;
    var tail = -half + 0.02f;
    foreach (var y in flame > 0f ? new[] { -NozzleY, NozzleY } : [])
    {
      drawList.AddTriangleFilled(Local(center, angle, h, tail, y - 0.17f), Local(center, angle, h, tail - flame, y),
        Local(center, angle, h, tail, y + 0.17f), U32(FlameOuter));
      drawList.AddTriangleFilled(Local(center, angle, h, tail, y - 0.09f), Local(center, angle, h, tail - flame * 0.55f, y),
        Local(center, angle, h, tail, y + 0.09f), U32(FlameInner));
    }
    if (ModImages.TryGetBuiltIn(ModImages.RocketImage, out var id, out _))
      drawList.AddImageQuad(id,
        Local(center, angle, h, -half, -0.5f), Local(center, angle, h, half, -0.5f),
        Local(center, angle, h, half, 0.5f), Local(center, angle, h, -half, 0.5f),
        new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
  }

  private static uint U32(Color color) => ImGui.ColorConvertFloat4ToU32((Vector4)color);
}
