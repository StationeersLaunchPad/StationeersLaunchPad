using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using StationeersLaunchPad.Sources;

namespace StationeersLaunchPad.Metadata;

// Statically scans mod assemblies (via Mono.Cecil, without executing them) for references to
// game members (Assembly-CSharp) that no longer exist. Those are what throw
// MissingMethodException / MissingFieldException at runtime, i.e. mods built against an older
// game version. Conservative on purpose: only flags removed types, or methods/fields whose
// name (and, for methods, parameter count) no longer exist, to limit false positives.
public static class ModCompatScanner
{
  private static readonly string[] GameAssemblyNames =
    { "Assembly-CSharp", "Assembly-CSharp-firstpass" };

  // Assemblies we never scan (frameworks / the game / the loader itself).
  private static readonly string[] SkipPrefixes =
  {
    "System", "Unity", "Mono.", "Mono.Cecil", "MonoMod", "0Harmony", "BepInEx",
    "Newtonsoft", "Cysharp", "UniTask", "netstandard", "mscorlib", "Assembly-CSharp",
    "StationeersLaunchPad", "LaunchPadBooster", "StationeersMods", "RG.ImGui", "ImGuiNET",
  };

  private static readonly object gate = new();
  private static Dictionary<string, List<string>> results = new(StringComparer.OrdinalIgnoreCase);
  private static readonly Dictionary<string, (long mtime, List<string> missing)> asmCache
    = new(StringComparer.OrdinalIgnoreCase);
  private static Assembly[] gameAsmCache;

  public static bool HasScanned { get; private set; }

  public static IReadOnlyList<string> GetMissing(ModInfo mod)
  {
    lock (gate)
      return results.TryGetValue(mod.DirectoryPath ?? "", out var m) ? m : Array.Empty<string>();
  }

  // Scans the given mods. Intended to run off the main thread (it does file IO + reflection).
  public static void ScanAll(IReadOnlyList<ModInfo> mods)
  {
    Assembly[] game;
    try
    {
      game = GameAssemblies();
    }
    catch (Exception ex)
    {
      Logger.Global.LogWarning($"compatibility scan unavailable: {ex.Message}");
      return;
    }

    var newResults = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    foreach (var mod in mods)
    {
      if (mod.Source == ModSourceType.Core || string.IsNullOrEmpty(mod.DirectoryPath))
        continue;

      var missing = new List<string>();
      foreach (var asmPath in mod.Assemblies)
      {
        try { missing.AddRange(ScanAssembly(asmPath, game)); }
        catch (Exception ex) { Logger.Global.LogDebug($"compat scan skipped {asmPath}: {ex.Message}"); }
      }

      if (missing.Count > 0)
        newResults[mod.DirectoryPath] = missing.Distinct().ToList();
    }

    lock (gate)
    {
      results = newResults;
      HasScanned = true;
    }
  }

  private static List<string> ScanAssembly(string path, Assembly[] game)
  {
    var name = Path.GetFileNameWithoutExtension(path);
    if (SkipPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
      return [];

    var mtime = File.GetLastWriteTimeUtc(path).Ticks;
    lock (gate)
      if (asmCache.TryGetValue(path, out var cached) && cached.mtime == mtime)
        return cached.missing;

    var missing = new List<string>();
    var seen = new HashSet<string>();

    using (var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters
    {
      ReadingMode = ReadingMode.Deferred,
      ReadSymbols = false,
    }))
    {
      var module = asm.MainModule;
      // Only mods that actually reference the game assembly can have game-API mismatches.
      if (module.AssemblyReferences.Any(r => GameAssemblyNames.Contains(r.Name)))
      {
        foreach (var type in module.GetTypes())
          foreach (var method in type.Methods)
          {
            if (!method.HasBody)
              continue;
            foreach (var instr in method.Body.Instructions)
              CheckOperand(instr.Operand, game, missing, seen);
          }
      }
    }

    lock (gate)
      asmCache[path] = (mtime, missing);
    return missing;
  }

  private static void CheckOperand(object operand, Assembly[] game, List<string> missing, HashSet<string> seen)
  {
    switch (operand)
    {
      case MethodReference mr:
      {
        if (mr.Name is ".ctor" or ".cctor")
          return;
        if (mr is GenericInstanceMethod || mr.HasGenericParameters)
          return;
        if (!IsGameType(mr.DeclaringType))
          return;

        var typeName = CleanTypeName(mr.DeclaringType);
        var gameType = ResolveType(game, typeName);
        if (gameType == null)
        {
          Report(missing, seen, $"{typeName} (type removed)");
          return;
        }
        if (!MethodExists(gameType, mr.Name, mr.Parameters.Count))
          Report(missing, seen, $"{typeName}.{mr.Name}()");
        return;
      }
      case FieldReference fr:
      {
        if (!IsGameType(fr.DeclaringType))
          return;

        var typeName = CleanTypeName(fr.DeclaringType);
        var gameType = ResolveType(game, typeName);
        if (gameType == null)
        {
          Report(missing, seen, $"{typeName} (type removed)");
          return;
        }
        if (!FieldExists(gameType, fr.Name))
          Report(missing, seen, $"{typeName}.{fr.Name}");
        return;
      }
    }
  }

  private static void Report(List<string> missing, HashSet<string> seen, string member)
  {
    if (seen.Add(member))
      missing.Add(member);
  }

  private static bool IsGameType(TypeReference type)
  {
    var scope = type?.Scope;
    return scope != null && GameAssemblyNames.Contains(scope.Name);
  }

  private static string CleanTypeName(TypeReference type)
  {
    var name = type.FullName;
    var generic = name.IndexOf('<');
    if (generic >= 0)
      name = name[..generic];
    return name.Replace('/', '+');
  }

  private static Type ResolveType(Assembly[] game, string fullName)
  {
    foreach (var asm in game)
    {
      try
      {
        var t = asm.GetType(fullName, false);
        if (t != null)
          return t;
      }
      catch { }
    }
    return null;
  }

  private const BindingFlags AllMembers =
    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

  private static bool MethodExists(Type type, string name, int paramCount)
  {
    try
    {
      return type.GetMethods(AllMembers).Any(m => m.Name == name && m.GetParameters().Length == paramCount);
    }
    catch { return true; } // on any uncertainty, don't flag
  }

  private static bool FieldExists(Type type, string name)
  {
    try
    {
      return type.GetFields(AllMembers).Any(f => f.Name == name);
    }
    catch { return true; }
  }

  private static Assembly[] GameAssemblies()
  {
    if (gameAsmCache != null)
      return gameAsmCache;
    gameAsmCache = AppDomain.CurrentDomain.GetAssemblies()
      .Where(a => GameAssemblyNames.Contains(a.GetName().Name))
      .ToArray();
    if (gameAsmCache.Length == 0)
      throw new InvalidOperationException("game assemblies not loaded");
    return gameAsmCache;
  }
}
