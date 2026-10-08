using ImGuiNET;
using UnityEngine;

namespace StationeersLaunchPad.UI;

// the startup progress bar. the rocket rides the load countdown, stands up while the mods load,
// climbs off the screen during the start countdown and dives into Load Mods when the menu opens
public static class RocketBar
{
  private const float Aspect = 256f / 59f;
  private const float NozzleY = 0.2f;

  private static readonly Color FlameOuter = LaunchPadTheme.Hex(0xF47A2A);
  private static readonly Color FlameInner = LaunchPadTheme.Hex(0xFFD35A);
  private static readonly Color Smoke = LaunchPadTheme.Hex(0x3A3D45);

  private const float DiveOutSeconds = 0.6f;
  private const float DiveInSeconds = 1.5f;
  private const float BoomSeconds = 0.35f;

  public enum Rocket { None, Pad, Burn, Hold }

  private sealed class Eased(float rate)
  {
    private float value;
    private int frame = -100;

    internal float Next(float target)
    {
      var fresh = UnityEngine.Time.frameCount - frame > 5;
      frame = UnityEngine.Time.frameCount;
      value = fresh ? target : value;
      return value = Mathf.Lerp(value, target, 1f - Mathf.Exp(-UnityEngine.Time.unscaledDeltaTime * rate));
    }
  }

  private static readonly Eased ride = new(14f);
  private static readonly Eased climb = new(14f);

  // where the rocket was drawn last, the dive starts there
  private static Vector2 lastCenter;
  private static float lastAngle;
  private static float lastHeight;
  private static int lastFrame = -100;

  private static float diveStart = -1f;
  private static Vector2 diveFrom;
  private static float diveAngle;
  private static float diveHeight;
  private static System.Action onDiveExit;

  // the Load Mods button, where the rocket ends up when it dives into the SLP menu
  public static Vector2? DiveTarget;

  public static bool Diving => diveStart >= 0f;

  public static float RowHeight => Mathf.Max(ImGui.GetTextLineHeight(), RocketHeight + 2f);
  private static float RocketHeight => Mathf.Round(ImGui.GetTextLineHeight() * 1.1f);

  // the bar with the rocket as its head, the full content width
  public static void Draw(float fraction, Rocket rocket, bool indeterminate = false)
  {
    var (min, max) = Bar();
    var drawList = ImGui.GetWindowDrawList();
    if (indeterminate)
    {
      var span = (max.x - min.x) * 0.18f;
      var t = Mathf.Repeat(UnityEngine.Time.unscaledTime * 0.6f, 1f);
      var from = Mathf.Lerp(min.x - span, max.x, t);
      drawList.PushClipRect(min, max, true);
      drawList.AddRectFilled(new Vector2(from, min.y), new Vector2(from + span, max.y), U32(LaunchPadTheme.Accent), (max.y - min.y) / 2f);
      drawList.PopClipRect();
      return;
    }

    fraction = Mathf.Clamp01(fraction);
    if (rocket == Rocket.None || Diving)
    {
      Fill(drawList, min, max, Mathf.Lerp(min.x, max.x, fraction));
      return;
    }

    var h = RocketHeight;
    var half = h * Aspect / 2f;
    var shown = ride.Next(fraction);
    // the nose is the head of the bar, the whole rocket stays inside it
    var center = new Vector2(Mathf.Lerp(min.x + half * 2f, max.x, shown) - half, (min.y + max.y) / 2f);
    Fill(drawList, min, max, center.x - half + h * 0.3f);
    DrawShip(drawList, center, 0f, h, rocket);
  }

  // the full bar with the rocket lying at its end, progress stands it up and takes it off the top of the screen
  public static void DrawLiftoff(float progress, Rocket rocket)
  {
    var (min, max) = Bar();
    Fill(ImGui.GetWindowDrawList(), min, max, max.x);
    if (Diving)
      return;

    var h = RocketHeight;
    var half = h * Aspect / 2f;
    var centerY = (min.y + max.y) / 2f;
    var lying = new Vector2(max.x - half, centerY);
    var standing = new Vector2(max.x - h * 0.6f, centerY - half);
    var u = climb.Next(Mathf.Clamp01(progress));
    var turn = Mathf.SmoothStep(0f, 1f, u / 0.2f);
    var lift = (standing.y + half * 2f + h * 2f) * Mathf.Pow(Mathf.Clamp01((u - 0.15f) / 0.8f), 1.8f);
    var center = Vector2.Lerp(lying, standing, turn) + new Vector2(0f, -lift);
    DrawShip(ImGui.GetForegroundDrawList(), center, -Mathf.PI / 2f * turn, h, rocket);
  }

  // the rocket drawn last frame dives off the splash, onExit opens the SLP menu, then it hits Load Mods
  public static void Dive(System.Action onExit)
  {
    if (Diving || UnityEngine.Time.frameCount - lastFrame > 5)
    {
      onExit();
      return;
    }
    diveStart = UnityEngine.Time.unscaledTime;
    diveFrom = lastCenter;
    diveAngle = lastAngle;
    diveHeight = lastHeight;
    onDiveExit = onExit;
  }

  // call once per frame from the splash, the dive flies over everything
  public static void DrawFlights()
  {
    if (!Diving)
      return;
    var t = UnityEngine.Time.unscaledTime - diveStart;
    var drawList = ImGui.GetForegroundDrawList();
    var h = diveHeight;
    var half = h * Aspect / 2f;
    var screen = ImGui.GetIO().DisplaySize;
    var target = DiveTarget ?? new Vector2(screen.x * 0.85f, screen.y * 0.9f);

    if (t < DiveOutSeconds)
    {
      // keeps its heading for a moment, then noses down off the bottom of the screen
      var exit = new Vector2(diveFrom.x + h * 3f, screen.y + half * 3f);
      Vector2[] path = [diveFrom, diveFrom + Heading(diveAngle) * h * 3f, new(exit.x, diveFrom.y), exit];
      Fly(drawList, path, Mathf.Pow(t / DiveOutSeconds, 1.4f), h);
    }
    else if (t < DiveOutSeconds + DiveInSeconds)
    {
      // the menu is open, the rocket comes back in at the top and swoops into the button
      if (onDiveExit != null)
      {
        onDiveExit();
        onDiveExit = null;
      }
      var entry = new Vector2(diveFrom.x + h * 3f, -half * 2f);
      Vector2[] path = [entry, entry + new Vector2(0f, screen.y * 0.45f), target + new Vector2(-screen.x * 0.35f, -screen.y * 0.05f), target];
      Fly(drawList, path, Glide((t - DiveOutSeconds) / DiveInSeconds), h);
    }
    else if (t < DiveOutSeconds + DiveInSeconds + BoomSeconds)
      DrawBoom(drawList, target, h, (t - DiveOutSeconds - DiveInSeconds) / BoomSeconds);
    else
      diveStart = -1f;
  }

  private static (Vector2 min, Vector2 max) Bar()
  {
    var width = ImGui.GetContentRegionAvail().x;
    var origin = ImGui.GetCursorScreenPos();
    ImGui.Dummy(new Vector2(width, RowHeight));
    var barHeight = Mathf.Max(3f, Mathf.Round(ImGui.GetTextLineHeight() * 0.22f));
    var centerY = origin.y + RowHeight / 2f;
    var min = new Vector2(origin.x, centerY - barHeight / 2f);
    var max = new Vector2(origin.x + width, centerY + barHeight / 2f);
    ImGui.GetWindowDrawList().AddRectFilled(min, max, LaunchPadTheme.OverU32(Color.white, 0.08f), barHeight / 2f);
    return (min, max);
  }

  private static void Fill(ImDrawListPtr drawList, Vector2 min, Vector2 max, float toX)
  {
    if (toX > min.x + 1f)
      drawList.AddRectFilled(min, new Vector2(Mathf.Min(toX, max.x), max.y), U32(LaunchPadTheme.Accent), (max.y - min.y) / 2f);
  }

  private static void Fly(ImDrawListPtr drawList, Vector2[] path, float u, float h)
  {
    var s = AtDistance(path, u);
    var dir = BezierTangent(path, s);
    var angle = Mathf.Atan2(dir.y, dir.x);
    var since = UnityEngine.Time.unscaledTime - diveStart;
    if (since < 0.25f)
      angle = Mathf.LerpAngle(diveAngle * Mathf.Rad2Deg, angle * Mathf.Rad2Deg, Mathf.SmoothStep(0f, 1f, since / 0.25f)) * Mathf.Deg2Rad;
    DrawShip(drawList, Bezier(path, s), angle, h, Rocket.Burn, 1.6f);
  }

  // 0 to 1 at twice the average speed at both ends, half of it in the middle
  private static float Glide(float u) => u * (2f * u * u - 3f * u + 2f);

  private static Vector2 Heading(float angle) => new(Mathf.Cos(angle), Mathf.Sin(angle));

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

  // the fireball cools to smoke and shrinks away. opaque only, translucent fills turn pink over the splash
  private static void DrawBoom(ImDrawListPtr drawList, Vector2 at, float h, float e)
  {
    var color = e < 0.3f ? Color.Lerp(FlameInner, FlameOuter, e / 0.3f) : Color.Lerp(FlameOuter, Smoke, (e - 0.3f) / 0.7f);
    var size = h * (1f + 3.5f * e) * (1f - e * e * e);
    for (var k = 0; k < 5; k++)
      drawList.AddCircleFilled(at + Heading(Mathf.PI * (k / 4f - 1f)) * size * 0.7f, size * 0.6f, U32(color), 16);
    drawList.AddCircleFilled(at, size * 0.6f, U32(Color.Lerp(FlameInner, color, e)), 16);
  }

  private static float Flicker(float time) => Mathf.PerlinNoise(time * 14f, 0.37f);

  // local coords are in rocket heights, x along the rocket
  private static Vector2 Local(Vector2 center, float angle, float h, float x, float y) =>
    center + new Vector2(x * Mathf.Cos(angle) - y * Mathf.Sin(angle), x * Mathf.Sin(angle) + y * Mathf.Cos(angle)) * h;

  // the sprite with a flame per nozzle, boost makes the flames longer
  private static void DrawShip(ImDrawListPtr drawList, Vector2 center, float angle, float h, Rocket rocket, float boost = 1f)
  {
    var flicker = Flicker(UnityEngine.Time.unscaledTime);
    var flame = rocket switch
    {
      Rocket.Burn => (0.9f + flicker * 0.4f) * boost,
      Rocket.Hold => 0.35f + flicker * 0.15f,
      _ => 0f,
    };
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

    if (Diving)
      return;
    lastCenter = center;
    lastAngle = angle;
    lastHeight = h;
    lastFrame = UnityEngine.Time.frameCount;
    var mouse = ImGui.GetMousePos();
    if ((mouse - center).magnitude < h * half)
      ImGuiHelper.TextTooltip("Ground crew reminder: don't stand in the exhaust. It's not a bastu.");
  }

  private static uint U32(Color color) => ImGui.ColorConvertFloat4ToU32((Vector4)color);
}
