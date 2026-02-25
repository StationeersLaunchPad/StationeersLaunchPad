
using BepInEx;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using StationeersLaunchPad.Loading;
using StationeersLaunchPad.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;

namespace StationeersLaunchPad
{
  public static class SLPRefCheck
  {
    private static HashSet<Assembly> offendingAssemblies = new();
    private static HashSet<LoadedMod> offendingMods = new();

    public static async UniTask RunRefCheck()
    {
      var refs = GetOffendingRefs();
      if (refs.Count != 0)
        await AddOffenders(refs);
    }

    private static Harmony harmony;
    private static HashSet<Assembly> GetOffendingRefs()
    {
      var tgtAssembly = typeof(SLPRefCheck).Assembly;
      var tgtName = tgtAssembly.FullName;
      var offending = new HashSet<Assembly>();

      foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
      {
        foreach (var asmRef in assembly.GetReferencedAssemblies())
          if (asmRef.FullName == tgtName)
            offending.Add(assembly);
      }

      harmony ??= new("SLPRefCheck");
      var patched = new List<MethodBase>();
      foreach (var method in Harmony.GetAllPatchedMethods())
      {
        if (method.DeclaringType.Assembly == tgtAssembly)
          patched.Add(method);
      }
      foreach (var method in patched)
      {
        var info = Harmony.GetPatchInfo(method);
        if (info.Finalizers.Count + info.ILManipulators.Count + info.Postfixes.Count +
            info.Prefixes.Count + info.Transpilers.Count == 0)
          continue;
        foreach (var patch in info.Finalizers)
          offending.Add(patch.PatchMethod.DeclaringType.Assembly);
        foreach (var patch in info.ILManipulators)
          offending.Add(patch.PatchMethod.DeclaringType.Assembly);
        foreach (var patch in info.Postfixes)
          offending.Add(patch.PatchMethod.DeclaringType.Assembly);
        foreach (var patch in info.Prefixes)
          offending.Add(patch.PatchMethod.DeclaringType.Assembly);
        foreach (var patch in info.Transpilers)
          offending.Add(patch.PatchMethod.DeclaringType.Assembly);

        if (method.DeclaringType == typeof(SLPRefCheck)
            || method.DeclaringType == typeof(RefWarningPanel))
          harmony.Unpatch(method, HarmonyPatchType.All);
      }

      return offending;
    }

    private static async UniTask AddOffenders(HashSet<Assembly> offenders)
    {
      await UniTask.SwitchToMainThread();

      offendingAssemblies ??= new();
      offendingMods ??= new();

      var assemblyToMod = new Dictionary<Assembly, LoadedMod>();
      for (var i = 0; i < ModLoader.LoadedMods.Count; i++)
      {
        var mod = ModLoader.LoadedMods[i];
        foreach (var assembly in mod.Assemblies)
          assemblyToMod[assembly] = mod;
      }

      var newAssemblies = new HashSet<Assembly>();
      foreach (var asm in offenders)
        if (offendingAssemblies.Add(asm))
          newAssemblies.Add(asm);

      var newMods = new List<LoadedMod>();
      foreach (var asm in newAssemblies)
      {
        if (assemblyToMod.TryGetValue(asm, out var mod))
        {
          if (offendingMods.Add(mod))
            newMods.Add(mod);
          continue;
        }
        var path = asm.IsDynamic ? "<dynamic>" : asm.Location;
        var message = $"Unknown assembly {asm.GetName().Name} at {path} contains unsupported references to StationeersLaunchPad.";
        Logger.Global.LogWarning(message);
        Compat.ConsoleWindowPrint(message);
      }

      if (newMods.Count == 0)
        return;

      foreach (var mod in newMods)
      {
        var message = $"Mod '{mod.Info.Name}' contains unsupported references to StationeersLaunchPad.";
        Logger.Global.LogWarning(message);
        Compat.ConsoleWindowPrint(message);
      }

      var previous = LoadUnsupportedCache() ?? new();
      var prevIds = new HashSet<string>();
      var prevHandles = new HashSet<ulong>();
      foreach (var mod in previous.Mods)
      {
        if (!string.IsNullOrEmpty(mod.ModID))
          prevIds.Add(mod.ModID);
        if (mod.WorkshopHandle > 1)
          prevHandles.Add(mod.WorkshopHandle);
      }

      var showWarning = previous.SLPVersion != LaunchPadInfo.VERSION;
      foreach (var mod in offendingMods)
      {
        if (!string.IsNullOrEmpty(mod.Info.ModID) && !prevIds.Contains(mod.Info.ModID))
          showWarning = true;
        if (mod.Info.WorkshopHandle > 1 && !prevHandles.Contains(mod.Info.WorkshopHandle))
          showWarning = true;
      }

      if (!showWarning)
        return;

      await RefWarningPanel.Show(offendingMods.Select(mod => mod.Info).ToList());
      SaveUnsupportedCache();
    }

    private static string UnsupportedCachePath => Path.Join(Paths.CachePath, "unsupported_mods.xml");
    private static UnsupportedData LoadUnsupportedCache()
    {
      try
      {
        if (!File.Exists(UnsupportedCachePath))
          return null;
        using var f = File.OpenRead(UnsupportedCachePath);
        return (UnsupportedData) new XmlSerializer(typeof(UnsupportedData)).Deserialize(f);
      }
      catch (Exception ex)
      {
        Logger.Global.LogDebug(ex.ToString());
      }
      return null;
    }

    private static void SaveUnsupportedCache()
    {
      var data = new UnsupportedData() { SLPVersion = LaunchPadInfo.VERSION };
      foreach (var mod in offendingMods)
      {
        data.Mods.Add(new()
        {
          ModID = mod.Info.ModID,
          WorkshopHandle = mod.Info.WorkshopHandle,
        });
      }
      try
      {
        using var f = File.OpenWrite(UnsupportedCachePath);
        new XmlSerializer(typeof(UnsupportedData)).Serialize(f, data);
      }
      catch (Exception ex)
      {
        Logger.Global.LogDebug(ex.ToString());
      }
    }

    [XmlRoot("UnsupportedMods")]
    public class UnsupportedData
    {
      [XmlAttribute]
      public string SLPVersion;
      [XmlElement("Mod")]
      public List<UnsupportedModData> Mods = new();
    }

    public class UnsupportedModData
    {
      [XmlAttribute("ModID")]
      public string ModID;
      [XmlAttribute("WorkshopHandle")]
      public ulong WorkshopHandle;
    }
  }
}