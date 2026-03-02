
using Assets.Scripts;
using Assets.Scripts.Objects;
using Assets.Scripts.Serialization;
using Assets.Scripts.Util;
using BepInEx;
using HarmonyLib;
using StationeersLaunchPad.Metadata;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Xml.Serialization;
using UnityEngine;

namespace StationeersLaunchPad
{
  public static class DebugPackage
  {
    public static string Export(string pkgpath = null)
    {
      try
      {
        if (string.IsNullOrEmpty(pkgpath))
          pkgpath = Path.Combine(
            LaunchPadPaths.SavePath,
            $"debugpkg_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.zip");
        else
        {
          if (!Path.IsPathRooted(pkgpath))
            pkgpath = Path.Combine(LaunchPadPaths.SavePath, pkgpath);
          if (!pkgpath.ToLower().EndsWith(".zip"))
            pkgpath += ".zip";
        }

        using (var archive = ZipFile.Open(pkgpath, ZipArchiveMode.Create))
        {
          archive.TryAddFile("modconfig.xml", LaunchPadPaths.ConfigPath);
          archive.TryAddFile("modrepos.xml", LaunchPadPaths.ModReposConfigPath);
          archive.TryAddFolder("BepInEx_config", Paths.ConfigPath, f => f.Name.ToLower().EndsWith(".cfg"));
          archive.TryWriteEntry("settings.xml", WriteSettings);
          archive.TryWriteEntry("sourceprefabs.txt", WritePrefabs(() => WorldManager.Instance?.SourcePrefabs));
          archive.TryWriteEntry("prefabs.txt", WritePrefabs(() => Prefab.AllPrefabs));
          archive.TryWriteEntry("userfiles.txt", WriteDirListing(LaunchPadPaths.SavePath));
          archive.TryWriteEntry("gamefiles.txt", WriteDirListing(LaunchPadPaths.GameRootPath));
          archive.TryWriteRecipes();
          archive.TryWriteEntry("assemblies.txt", WriteAssemblies);
          archive.TryWriteEntry("patches.txt", WritePatches);
          archive.TryAddModAbouts();
          archive.TryWriteEntry("systeminfo.txt", WriteSystemInfo);

          archive.TryWriteEntry("console.log", WriteConsoleLog);
          archive.TryAddFile("player.log", Application.consoleLogPath);
        }

        if (!Platform.IsServer)
          ProcessUtil.OpenExplorerSelectFile(pkgpath);
        return $"exported {pkgpath}";
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
        return ex.ToString();
      }
    }

    private static void TryAddFile(this ZipArchive archive, string name, string path)
    {
      if (!File.Exists(path))
      {
        Logger.Global.LogDebug($"skipping non-existing file {name} at {path}");
        return;
      }
      try
      {
        // CreateEntryFromFile tries to open with FileShare.Read which fails on the log file
        using var writer = archive.CreateEntry(name).Open();
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        reader.CopyTo(writer);
      }
      catch (Exception ex)
      {
        Logger.Global.LogDebug($"error adding file {path}: {ex}");
      }
    }

    private static void TryAddFolder(this ZipArchive archive, string name, string path, Func<FileInfo, bool> filter)
    {
      try
      {
        if (!Directory.Exists(path))
        {
          Logger.Global.LogDebug($"skipping non-existing directory {name} at {path}");
          return;
        }
        foreach (var file in new DirectoryInfo(path).GetFiles())
        {
          if (!filter(file))
            continue;
          archive.TryAddFile(Path.Join(name, file.Name), file.FullName);
        }
      }
      catch (Exception ex)
      {
        Logger.Global.LogDebug($"error adding folder {path}: {ex}");
      }
    }

    private static void TryWriteEntry(this ZipArchive archive, string name, Action<StreamWriter> fn)
    {
      try
      {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        fn(writer);
      }
      catch (Exception ex)
      {
        Logger.Global.LogDebug($"error adding text file {name}: {ex}");
      }
    }

    private static void WriteXml(StreamWriter f, object obj)
    {
      if (obj is null)
      {
        f.WriteLine($"<null />");
        return;
      }
      new XmlSerializer(obj.GetType()).Serialize(f, obj);
    }

    private static void WriteSettings(StreamWriter f)
    {
      var data = Settings.CurrentData;
      var secrets = new Dictionary<FieldInfo, object>();
      foreach (var field in typeof(Settings.SettingData).GetFields(BindingFlags.Instance | BindingFlags.Public))
      {
        var name = field.Name.ToLower();
        if (name.Contains("pass") || name.Contains("secret") || name.Contains("token"))
        {
          secrets[field] = field.GetValue(data);
          field.SetValue(data, null);
        }
      }
      try
      {
        WriteXml(f, data);
      }
      finally
      {
        foreach (var (field, value) in secrets)
          field.SetValue(data, value);
      }
    }

    private static Action<StreamWriter> WritePrefabs(Func<List<Thing>> getPrefabs) => f =>
    {
      var prefabs = getPrefabs();
      if (prefabs is null)
      {
        f.WriteLine("prefabs list is <null>");
        return;
      }
      for (var i = 0; i < prefabs.Count; i++)
      {
        var prefab = prefabs[i];
        if (prefab is null)
        {
          f.WriteLine($"{i:D4}: <null>");
          continue;
        }
        f.WriteLine($"{i:D4}: {prefab.name} {prefab.PrefabHash} {prefab.GetType()}");
      }
    };

    private static Action<StreamWriter> WriteDirListing(string path) => f =>
    {
      var indent = "  ";
      f.WriteLine(path);
      walkDir(new(path));

      void walkDir(DirectoryInfo dir)
      {
        var prevIndent = indent;
        indent += "  ";
        try
        {
          foreach (var subdir in dir.GetDirectories())
          {
            f.WriteLine($"{indent}{subdir.Name}/");
            walkDir(subdir);
          }
          foreach (var file in dir.GetFiles())
          {
            f.WriteLine($"{indent}{file.Name} {file.Length}b");
          }
        }
        catch (Exception ex)
        {
          var lines = ex.ToString().Split("\n");
          foreach (var line in lines)
            f.WriteLine($"{indent}{line}");
        }
        finally
        {
          indent = prevIndent;
        }
      }
    };

    private const BindingFlags ANY_FLAGS =
      BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static void TryWriteRecipes(this ZipArchive archive)
    {
      try
      {
        var field = typeof(WorldManager).GetField("_recipeComparables", ANY_FLAGS);
        foreach (var (key, recipes) in (Dictionary<RecipeType, RecipeComparable>) field.GetValue(null))
        {
          archive.TryWriteEntry(Path.Join("recipes", $"{key}.txt"), f => WriteRecipes(f, recipes));
        }
      }
      catch (Exception ex)
      {
        Logger.Global.LogDebug($"failed to write recipes: {ex}");
      }
    }
    private static void WriteRecipes(StreamWriter f, RecipeComparable recipes)
    {
      var type = recipes.GetType();
      f.WriteLine($"{type}");
      if (recipes is not RecipeDataComparable)
        return;
      var field = typeof(RecipeDataComparable).GetField("_recipeDataList", ANY_FLAGS);
      var rlist = (List<WorldManager.RecipeData>) field.GetValue(recipes);
      f.WriteLine($"{rlist.Count} recipes");
      for (var i = 0; i < rlist.Count; i++)
      {
        f.WriteLine($"{i:D4}: {rlist[i].PrefabName}");
      }
    }

    private static void WriteConsoleLog(StreamWriter writer)
    {
      var lines = ConsoleWindow.ConsoleBuffer;
      for (var i = lines.Length; --i >= 0;)
      {
        var line = lines[i];
        if (string.IsNullOrEmpty(line.Text))
          continue;
        writer.WriteLine(line.ToString());
      }
    }

    private static void WriteAssemblies(StreamWriter writer)
    {
      foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
      {
        var path = asm.IsDynamic ? "<dynamic>" : asm.Location;
        writer.WriteLine($"{asm.GetName().FullName} {path}");
      }
    }

    private static void WritePatches(StreamWriter writer)
    {
      void addPatches(string type, ReadOnlyCollection<Patch> patches)
      {
        if (patches is null || patches.Count == 0)
          return;
        writer.WriteLine($"  {type}");
        foreach (var patch in patches)
        {
          writer.WriteLine($"    {patch.PatchMethod.FullDescription()} in {patch.PatchMethod.DeclaringType.Assembly.FullName}");
        }
      }
      foreach (var target in Harmony.GetAllPatchedMethods())
      {
        writer.WriteLine($"{target.FullDescription()} in {target.DeclaringType.Assembly.FullName}");
        var patches = Harmony.GetPatchInfo(target);
        addPatches("Prefix", patches.Prefixes);
        addPatches("Postfix", patches.Postfixes);
        addPatches("Transpiler", patches.Transpilers);
        addPatches("Finalizer", patches.Finalizers);
        addPatches("ILManipulator", patches.ILManipulators);
      }
    }

    private static void TryAddModAbouts(this ZipArchive archive)
    {
      try
      {
        var config = ModConfigUtil.LoadConfig();
        for (var i = 0; i < config.Mods.Count; i++)
        {
          var mod = config.Mods[i];
          if (mod is CoreModData)
            continue;
          var name = Platform.MakeValidFileName($"{i:D3}_{mod.GetType().Name}_{mod.GetAboutData()?.Name}");
          if (name.Length > 64)
            name = name[..64];
          archive.TryAddFile(Path.Join("mods", $"{name}.xml"), mod.AboutXmlPath);
        }
      }
      catch (Exception ex)
      {
        Logger.Global.LogDebug($"failed to add mod abouts: {ex}");
      }
    }

    private static void WriteSystemInfo(StreamWriter writer)
    {
      writer.WriteLine($"graphicsDeviceID: {SystemInfo.graphicsDeviceID:X4}");
      writer.WriteLine($"graphicsDeviceName: {SystemInfo.graphicsDeviceName}");
      writer.WriteLine($"graphicsDeviceVendor: {SystemInfo.graphicsDeviceVendor}");
      writer.WriteLine($"graphicsDeviceVendorID: {SystemInfo.graphicsDeviceVendorID:X4}");
      writer.WriteLine($"graphicsDeviceVersion: {SystemInfo.graphicsDeviceVersion}");
      writer.WriteLine($"graphicsMemorySize: {SystemInfo.graphicsMemorySize}");
      writer.WriteLine($"processorType: {SystemInfo.processorType}");
      writer.WriteLine($"processorCount: {SystemInfo.processorCount}");
      writer.WriteLine($"systemMemorySize: {SystemInfo.systemMemorySize}");
    }
  }
}