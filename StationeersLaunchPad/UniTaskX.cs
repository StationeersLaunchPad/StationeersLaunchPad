
using Cysharp.Threading.Tasks;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace StationeersLaunchPad
{
  public static class UniTaskX
  {
    private delegate UniTask WaitWhile1(Func<bool> predicate, PlayerLoopTiming timing, CancellationToken cancellationToken);
    private delegate UniTask WaitWhile2(Func<bool> predicate, PlayerLoopTiming timing, CancellationToken cancellationToken, bool cancelImmediately);
    private static WaitWhile1 WaitWhileFn;
    public static UniTask WaitWhile(Func<bool> predicate, PlayerLoopTiming timing = PlayerLoopTiming.Update, CancellationToken cancellationToken = default)
    {
      static WaitWhile1 wrap(WaitWhile2 fn) => (p, t, c) => fn(p, t, c, false);
      WaitWhileFn ??= MakeCompatDelegate<WaitWhile1, WaitWhile2>("WaitWhile", wrap);
      return WaitWhileFn(predicate, timing, cancellationToken);
    }

    private static bool SignaturesMatch(MethodInfo f1, MethodInfo f2)
    {
      if (f1.ReturnType != f2.ReturnType)
        return false;
      var ps1 = f1.GetParameters();
      var ps2 = f2.GetParameters();
      if (ps1.Length != ps2.Length)
        return false;
      for (var i = 0; i < ps1.Length; i++)
      {
        var p1 = ps1[i];
        var p2 = ps2[i];
        if (p1.ParameterType != p2.ParameterType)
          return false;
        if (p1.IsOut != p2.IsOut)
          return false;
        if (p1.IsRetval != p2.IsRetval)
          return false;
      }
      return true;
    }

    private static F MakeCompatDelegate<F, F2>(string name, Func<F2, F> wrap)
      where F : Delegate
      where F2 : Delegate
    {
      var methods = typeof(UniTask).GetMethods(BindingFlags.Public | BindingFlags.Static);
      var fmethod = typeof(F).GetMethod("Invoke");
      var f2method = typeof(F2).GetMethod("Invoke");

      var fmatch = methods.FirstOrDefault(f => f.Name == name && SignaturesMatch(f, fmethod));
      if (fmatch != null)
        return (F) fmatch.CreateDelegate(typeof(F));

      var f2match = methods.FirstOrDefault(f => f.Name == name && SignaturesMatch(f, f2method))
        ?? throw new InvalidOperationException($"Could not find UniTask.{name} matching {typeof(F)} or {typeof(F2)}");

      var f2 = (F2) f2match.CreateDelegate(typeof(F2));
      return wrap(f2);
    }
  }
}