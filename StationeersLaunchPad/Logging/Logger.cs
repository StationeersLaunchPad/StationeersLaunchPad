using System;
using UnityEngine;

// This is not moved to another package because the import would conflict with UnityEngine.Logger
namespace StationeersLaunchPad
{
  [Flags]
  public enum LogSeverity
  {
    Debug = 1 << 0,
    Information = 1 << 1,
    Warning = 1 << 2,
    Error = 1 << 3,
    Exception = 1 << 4,
    Fatal = 1 << 5,
    All = Debug | Information | Warning | Error | Exception | Fatal,
  }

  public class Logger
  {
    public static readonly Logger Global = new("Global");

    public readonly string Name;
    public readonly Logger Parent;
    public readonly LogBuffer Buffer;

    public int Count => this.Buffer.Count;

    public ulong TotalCount => this.Buffer.TotalCount;

    public bool IsChild => this.Parent != null;
    public LogLine this[int index] => this.At(index);

    public Logger(string name = "", Logger parent = null, int size = LogBuffer.DEFAULT_BUFFER_SIZE)
    {
      this.Name = name;
      this.Parent = parent;
      this.Buffer = new LogBuffer(this.IsChild ? size / 2 : size);
    }

    public Logger(string name, LogBuffer buffer, Logger parent = null)
    {
      this.Name = name;
      this.Parent = parent;
      this.Buffer = buffer;
    }

    public Logger CreateChild(string name) => new Logger(name, this);

    public void Clear() => this.Buffer.Clear();
    public void CopyToClipboard() => this.Buffer.CopyToClipboard();
    public LogLine At(int index) => this.Buffer[index];
    public LogLine First() => this.Buffer[0];
    public LogLine Last() => this.Buffer[this.Count - 1];

    public void Log(string message, LogSeverity severity = LogSeverity.Information, bool unity = true, string name = "")
    {
      name = string.IsNullOrWhiteSpace(name) ? this.Name : name;
      this.Buffer.Add(name, message, severity);

      if (this.IsChild)
      {
        this.Parent?.Log(message, severity, unity, name);
      }
      else if (unity)
      {
        this.LogUnity(message, severity switch
        {
          LogSeverity.Debug or LogSeverity.Information => LogType.Log,
          LogSeverity.Warning => LogType.Warning,
          LogSeverity.Error or LogSeverity.Fatal => LogType.Error,
          LogSeverity.Exception => LogType.Exception,
          _ => LogType.Log
        }, name);
      }
    }

    public void Log(Exception exception, bool unity = true, string name = "")
    {
      name = string.IsNullOrWhiteSpace(name) ? this.Name : name;
      this.Buffer.Add(name, exception);

      if (this.IsChild)
        this.Parent?.Log(exception, unity, name);
      else if (unity)
        this.LogUnity(exception);
    }

    private void LogUnityInternal(string message, LogType type, string name) => Debug.LogFormat(type, LogOption.None, null, $"[{name}]: {message}");

    public void LogUnity(string message, LogType severity = LogType.Log, string name = null)
    {
      name ??= this.Name;
      this.LogUnityInternal(message, severity, name);
    }

    public void LogUnity(Exception exception) => Debug.LogException(exception);

    public void LogUnityAssert(string message) => this.LogUnity(message, LogType.Assert, this.Name);
    public void LogUnityWarning(string message) => this.LogUnity(message, LogType.Warning, this.Name);
    public void LogUnityError(string message) => this.LogUnity(message, LogType.Error, this.Name);
    public void LogUnityException(Exception exception) => this.LogUnity(exception);

    public void LogDebug(string message, bool unity = true) => this.Log(message, LogSeverity.Debug, unity);
    public void LogInfo(string message, bool unity = true) => this.Log(message, LogSeverity.Information, unity);
    public void LogWarning(string message, bool unity = true) => this.Log(message, LogSeverity.Warning, unity);
    public void LogError(string message, bool unity = true) => this.Log(message, LogSeverity.Error, unity);
    public void LogException(Exception exception, bool unity = true) => this.Log(exception, unity);
    public void LogFatal(string message, bool unity = true) => this.Log(message, LogSeverity.Fatal, unity);

    public void LogFormat(bool unity, LogSeverity severity, string format, params object[] args) => this.Log(string.Format(format, args), severity, unity);
    public void LogDebugFormat(bool unity, string format, params object[] args) => this.LogFormat(unity, LogSeverity.Debug, format, args);
    public void LogInfoFormat(bool unity, string format, params object[] args) => this.LogFormat(unity, LogSeverity.Information, format, args);
    public void LogWarningFormat(bool unity, string format, params object[] args) => this.LogFormat(unity, LogSeverity.Warning, format, args);
    public void LogErrorFormat(bool unity, string format, params object[] args) => this.LogFormat(unity, LogSeverity.Error, format, args);
    public void LogFatalFormat(bool unity, string format, params object[] args) => this.LogFormat(unity, LogSeverity.Fatal, format, args);
  }
}