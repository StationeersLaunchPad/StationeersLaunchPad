using System;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace StationeersLaunchPad
{
  public class Logger
  {
    public const int BUFFER_SIZE = 300;
    public static readonly Logger Global = new();

    public readonly string Prefix;
    public readonly Logger Parent;
    public readonly LineBuffer Lines = new(BUFFER_SIZE);

    public Logger(string prefix = "", Logger parent = null)
    {
      this.Prefix = prefix;
      this.Parent = parent;
    }

    public void Log(string message, LogType logType = LogType.Log, bool logUnity = true)
    {
      this.Lines.AddLine(message, logType);
      var fullMessage = $"{Prefix}{message}";
      if (this.Parent != null)
        this.Parent.Log(fullMessage, logType, logUnity);
      else if (logUnity)
        Debug.LogFormat(logType, LogOption.None, null, fullMessage);
    }

    public void Log(string message)
    {
      this.Log(message, LogType.Log, true);
    }

    public void LogDebug(string message) {
      if (LaunchPadConfig.Debug)
        this.Log(message, LogType.Log, true);
    }

    public void LogWarning(string message)
    {
      this.Log(message, LogType.Warning, true);
    }

    public void LogError(string message)
    {
      this.Log(message, LogType.Error, true);
    }

    public void LogException(Exception ex, bool logUnity = true)
    {
      this.Lines.AddException(ex, "");
      var parent = this.Parent;
      var prefix = this.Prefix;
      while (parent != null)
      {
        parent.Lines.AddException(ex, prefix);
        prefix = parent.Prefix + prefix;
        parent = parent.Parent;
      }
      if (logUnity)
        Debug.LogException(ex);
    }

    public void LogFormat(LogType logType, string format, params object[] args)
    {
      this.Log(string.Format(format, args), logType, true);
    }

    public void LogFormatSkip(LogType logType, string format, params object[] args)
    {
      this.Log(string.Format(format, args), logType, false);
    }

    public Logger WithPrefix(string prefix)
    {
      return new Logger($"{this.Prefix}{prefix}", this);
    }

    public class LineBuffer
    {
      private readonly object _lock = new();
      private readonly LogLine[] lines;
      public int Count { get; private set; }
      public int TotalCount { get; private set; }
      private int start = 0;
      public LineBuffer(int bufferSize)
      {
        this.lines = new LogLine[bufferSize];
      }

      public void AddLine(string line, LogType logType)
      {
        var logLine = new LogLine
        {
          Text = line,
          Type = logType,
        };
        lock (this._lock)
        {
          if (this.Count == this.lines.Length)
          {
            this.lines[this.start] = logLine;
            this.start = (this.start + 1) % this.lines.Length;
          }
          else
          {
            this.lines[this.Count] = logLine;
            this.Count++;
          }
          this.TotalCount++;
        }
      }

      public void AddException(Exception ex, string prefix)
      {
        var stackTrace = (ex.StackTrace ?? "").Split('\n');
        lock (this._lock)
        {
          this.AddLine($"{prefix}{ex.Message}", LogType.Exception);
          foreach (var line in stackTrace)
          {
            this.AddLine($"{prefix}  {line.Trim()}", LogType.Exception);
          }
        }
      }

      public LogLine this[int index] => this.lines[(index + this.start) % this.lines.Length];
    }
  }

  public class LogLine
  {
    public string Text;
    public LogType Type;
  }

  public class LogWrapper : ILogHandler
  {
    public readonly ILogHandler Inner;
    public LogWrapper(ILogHandler inner)
    {
      this.Inner = inner;
    }

    public void LogException(Exception exception, UnityEngine.Object context)
    {
      this.Inner.LogException(exception, context);
      if (ModLoader.TryGetExecutingMod(out var mod))
        mod.Logger.LogException(exception, false);
      else if (ModLoader.TryGetStackTraceMod(new StackTrace(exception), out var mod2))
        mod2.Logger.LogException(exception, false);
    }

    public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
    {
      this.Inner.LogFormat(logType, context, format, args);
      if (ModLoader.TryGetExecutingMod(out var mod))
        mod.Logger.LogFormatSkip(logType, format, args);
    }
  }
}