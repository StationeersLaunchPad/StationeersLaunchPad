using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ImGuiNET;
using StationeersLaunchPad.Metadata;
using UnityEngine;

namespace StationeersLaunchPad.UI;

// lazily loads workshop preview images (preview.png or thumb.png in About)
public static class ModImages
{
  // previews are often 1024px+, keep a smaller mipmapped copy
  private const int MaxSize = 384;
  // decoding a PNG takes a few ms, spread the first load over frames
  private const int LoadsPerFrame = 2;

  private static readonly string[] Candidates = ["preview", "thumb"];
  private static readonly string[] Extensions = [".png", ".jpg", ".jpeg"];

  private static readonly Dictionary<string, Texture> cache = new(StringComparer.OrdinalIgnoreCase);
  private static int loadFrame = -1;
  private static int loadsThisFrame;

  // not referenced at compile time, the dedicated server doesn't ship them
  private static MethodInfo loadImage;
  private static FieldInfo textureManagerField;
  private static MethodInfo textureManagerGetId;
  private static object textureManager;
  private static Func<Texture, int> getTextureIdBound;
  private static bool resolved;
  private static bool available;

  // the game resolves texture ids through its own ImGuiManager, not ImGuiUn's context
  private static int getTextureId(Texture texture)
  {
    var manager = textureManagerField.GetValue(null);
    if (manager == null)
      return -1;
    if (!ReferenceEquals(manager, textureManager))
    {
      textureManager = manager;
      getTextureIdBound = (Func<Texture, int>)Delegate.CreateDelegate(
        typeof(Func<Texture, int>), manager, textureManagerGetId);
    }
    return getTextureIdBound(texture);
  }

  public static bool TryGet(ModInfo mod, out IntPtr textureId, out Vector2 size)
  {
    textureId = IntPtr.Zero;
    size = Vector2.zero;
    var dir = mod?.DirectoryPath;
    if (string.IsNullOrEmpty(dir) || !Resolve())
      return false;

    if (!cache.TryGetValue(dir, out var texture))
    {
      if (Time.frameCount != loadFrame)
      {
        loadFrame = Time.frameCount;
        loadsThisFrame = 0;
      }
      if (loadsThisFrame >= LoadsPerFrame)
        return false;
      loadsThisFrame++;
      texture = Load(dir);
      cache[dir] = texture;
    }
    if (texture is RenderTexture rt && !rt.IsCreated())
    {
      // render textures can be lost (device reset); reload on next request
      cache.Remove(dir);
      UnityEngine.Object.Destroy(rt);
      return false;
    }
    if (texture == null)
      return false;

    var id = getTextureId(texture);
    textureId = (IntPtr)id;
    size = new(texture.width, texture.height);
    return id > 0;
  }

  // fills rect with the mod's image, or its initials when it has none
  public static void DrawFill(ImDrawListPtr drawList, ModInfo mod, Vector2 min, Vector2 max)
  {
    var box = max - min;
    if (TryGet(mod, out var id, out var size))
    {
      // crop to the box's aspect ratio from the image center
      var boxAspect = box.x / box.y;
      var imgAspect = size.x / size.y;
      Vector2 uv0 = Vector2.zero, uv1 = Vector2.one;
      if (imgAspect > boxAspect)
      {
        var cut = (1f - boxAspect / imgAspect) / 2f;
        uv0.x = cut;
        uv1.x = 1f - cut;
      }
      else if (imgAspect < boxAspect)
      {
        var cut = (1f - imgAspect / boxAspect) / 2f;
        uv0.y = cut;
        uv1.y = 1f - cut;
      }
      drawList.AddImage(id, min, max, uv0, uv1);
      return;
    }

    drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32((Vector4)LaunchPadTheme.Panel));
    var initials = Initials(mod?.Name);
    var textSize = ImGui.CalcTextSize(initials);
    drawList.AddText(min + (box - textSize) / 2f,
      ImGui.ColorConvertFloat4ToU32((Vector4)LaunchPadTheme.TextMuted), initials);
  }

  private static string Initials(string name)
  {
    if (string.IsNullOrWhiteSpace(name))
      return "?";
    var words = name.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
      .Where(word => char.IsLetterOrDigit(word[0]))
      .ToList();
    if (words.Count == 0)
      return name.Substring(0, 1).ToUpperInvariant();
    if (words.Count == 1)
      return words[0].Substring(0, Math.Min(2, words[0].Length)).ToUpperInvariant();
    return $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}";
  }

  private static bool Resolve()
  {
    if (resolved)
      return available;
    resolved = true;
    try
    {
      loadImage = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")
        ?.GetMethod("LoadImage", [typeof(Texture2D), typeof(byte[]), typeof(bool)]);
      textureManagerField = Type.GetType("Assets.Scripts.UI.ImGuiManager, Assembly-CSharp")
        ?.GetField("igTextureManager", BindingFlags.Public | BindingFlags.Static);
      textureManagerGetId = textureManagerField?.FieldType.GetMethod(
        "GetTextureId", BindingFlags.Public | BindingFlags.Instance, null, [typeof(Texture)], null);
      available = loadImage != null && textureManagerGetId != null;
    }
    catch (Exception ex)
    {
      Logger.Global.LogDebug($"Mod images unavailable: {ex.Message}");
      available = false;
    }
    if (!available)
      Logger.Global.LogDebug("Mod images unavailable: image loading or ImGui textures not found");
    return available;
  }

  private static Texture Load(string modDir)
  {
    var path = FindImage(modDir);
    if (path == null)
      return null;

    Texture2D source = null;
    try
    {
      source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
      if (!(bool)loadImage.Invoke(null, [source, File.ReadAllBytes(path), false]))
        return null;
      return Shrink(source);
    }
    catch (Exception ex)
    {
      Logger.Global.LogDebug($"Failed to load mod image {path}: {ex.Message}");
      return null;
    }
    finally
    {
      if (source != null)
        UnityEngine.Object.Destroy(source);
    }
  }

  private static string FindImage(string modDir)
  {
    var aboutDir = Path.Combine(modDir, "About");
    if (!Directory.Exists(aboutDir))
      return null;
    string[] files;
    try
    {
      files = Directory.GetFiles(aboutDir);
    }
    catch (Exception)
    {
      return null;
    }
    foreach (var candidate in Candidates)
      foreach (var ext in Extensions)
      {
        var match = files.FirstOrDefault(file =>
          Path.GetFileName(file).Equals(candidate + ext, StringComparison.OrdinalIgnoreCase));
        if (match != null)
          return match;
      }
    return null;
  }

  private static Texture Shrink(Texture2D source)
  {
    var scale = Math.Min(1f, (float)MaxSize / Math.Max(source.width, source.height));
    var width = Math.Max(1, Mathf.RoundToInt(source.width * scale));
    var height = Math.Max(1, Mathf.RoundToInt(source.height * scale));
    var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
    {
      useMipMap = true,
      autoGenerateMips = true,
      filterMode = FilterMode.Trilinear,
      wrapMode = TextureWrapMode.Clamp,
    };
    target.Create();
    Graphics.Blit(source, target);
    return target;
  }
}
