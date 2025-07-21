using System;
using System.Diagnostics;
using UnityEngine;

namespace StationeersLaunchPad
{
  public class LogWrapper : ILogHandler
  {
    public readonly ILogHandler Inner;
    public LogWrapper(ILogHandler inner)
    {
      this.Inner = inner;
    }

    private bool IsLaunchpadLog()
    {
      var st = new StackTrace(2);
      for (var i = 0; i < st.FrameCount; i++)
      {
        var frame = st.GetFrame(i);
        if (frame.GetMethod().DeclaringType == typeof(Logger))
          return true;
      }
      return false;
    }

    public void LogException(Exception exception, UnityEngine.Object context)
    {
      this.Inner.LogException(exception, context);
      if (IsLaunchpadLog())
        return;
      if (ModLoader.TryGetExecutingMod(out var mod))
        mod.Logger.LogException(exception, false);
      else if (ModLoader.TryGetStackTraceMod(new StackTrace(exception), out var mod2))
        mod2.Logger.LogException(exception, false);
    }

    public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
    {
      this.Inner.LogFormat(logType, context, format, args);
      if (IsLaunchpadLog())
        return;
      if (ModLoader.TryGetExecutingMod(out var mod))
        mod.Logger.LogInfoFormat(false, format, args);
    }
  }
}
