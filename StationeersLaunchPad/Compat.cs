
using Assets.Scripts;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace StationeersLaunchPad
{
  public static class Compat
  {
    private delegate void InvokeAction(params object[] args);

    private static InvokeAction _ConsoleWindowPrint = null;
    public static void ConsoleWindowPrint(string message)
    {
      try
      {
        _ConsoleWindowPrint ??= MakeCompatAction(typeof(ConsoleWindow), "Print", typeof(string));
      }
      catch (Exception ex)
      {
        Logger.Global.LogException(ex);
        _ConsoleWindowPrint = args => UnityEngine.Debug.Log(string.Join(", ", args));
      }
      _ConsoleWindowPrint(message);
    }

    private static InvokeAction MakeCompatAction(Type type, string name, params Type[] ptypes)
    {
      MethodInfo match = null;
      ParameterInfo[] mparams = null;
      foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public))
      {
        if (method.Name != name)
          continue;
        mparams = method.GetParameters();
        if (mparams.Length < ptypes.Length)
          continue;
        var valid = true;
        for (var i = 0; i < ptypes.Length; i++)
          valid &= ptypes[i] == mparams[i].ParameterType;
        if (!valid)
          continue;
        for (var i = ptypes.Length; i < mparams.Length; i++)
          valid &= mparams[i].HasDefaultValue;
        if (!valid)
          continue;
        match = method;
        break;
      }
      if (match == null)
        throw new InvalidOperationException($"Could not find match for {type}.{name}");
      var defaults = new List<object>();
      for (var i = ptypes.Length; i < mparams.Length; i++)
        defaults.Add(mparams[i].DefaultValue);
      return args =>
      {
        var fullArgs = new object[args.Length + defaults.Count];
        for (var i = 0; i < args.Length; i++)
          fullArgs[i] = args[i];
        for (var i = 0; i < defaults.Count; i++)
          fullArgs[i + args.Length] = defaults[i];
        match.Invoke(null, fullArgs);
      };
    }
  }
}