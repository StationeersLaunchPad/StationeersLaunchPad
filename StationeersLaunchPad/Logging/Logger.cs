using System;
using UnityEngine;

// This is not moved to another package because the import would conflict with UnityEngine.Logger
namespace StationeersLaunchPad;

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

  public int Count => Buffer.Count;

  public ulong TotalCount => Buffer.TotalCount;

  public bool IsChild => Parent != null;
  public LogLine this[int index] => At(index);

  public Logger(string name = "", Logger parent = null, int size = LogBuffer.DEFAULT_BUFFER_SIZE)
  {
    Name = name;
    Parent = parent;
    Buffer = new(IsChild ? size / 2 : size);
  }

  public Logger(string name, LogBuffer buffer, Logger parent = null)
  {
    Name = name;
    Parent = parent;
    Buffer = buffer;
  }

  public Logger CreateChild(string name) => new(name, this);

  public void Clear() => Buffer.Clear();
  public void CopyToClipboard() => Buffer.CopyToClipboard();
  public LogLine At(int index) => Buffer[index];
  public LogLine First() => Buffer[0];
  public LogLine Last() => Buffer[Count - 1];

  public void Log(string message, LogSeverity severity = LogSeverity.Information, bool unity = true, string name = "")
  {
    name = string.IsNullOrWhiteSpace(name) ? Name : name;
    Buffer.Add(name, message, severity);

    if (IsChild)
      Parent?.Log(message, severity, unity, name);
    else if (unity)
    {
      LogUnity(message, severity switch
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
    name = string.IsNullOrWhiteSpace(name) ? Name : name;
    Buffer.Add(name, exception);

    if (IsChild)
      Parent?.Log(exception, unity, name);
    else if (unity)
      LogUnity(exception);
  }

  private void LogUnityInternal(string message, LogType type, string name) =>
    Debug.LogFormat(type, LogOption.None, null, $"[{name}]: {message}");

  public void LogUnity(string message, LogType severity = LogType.Log, string name = null)
  {
    name ??= Name;
    LogUnityInternal(message, severity, name);
  }

  public void LogUnity(Exception exception) => Debug.LogException(exception);

  public void LogUnityAssert(string message) => LogUnity(message, LogType.Assert, Name);
  public void LogUnityWarning(string message) => LogUnity(message, LogType.Warning, Name);
  public void LogUnityError(string message) => LogUnity(message, LogType.Error, Name);
  public void LogUnityException(Exception exception) => LogUnity(exception);

  public void LogDebug(string message, bool unity = true) => Log(message, LogSeverity.Debug, unity);
  public void LogInfo(string message, bool unity = true) => Log(message, LogSeverity.Information, unity);
  public void LogWarning(string message, bool unity = true) => Log(message, LogSeverity.Warning, unity);
  public void LogError(string message, bool unity = true) => Log(message, LogSeverity.Error, unity);
  public void LogException(Exception exception, bool unity = true) => Log(exception, unity);
  public void LogFatal(string message, bool unity = true) => Log(message, LogSeverity.Fatal, unity);

  public void LogFormat(bool unity, LogSeverity severity, string format, params object[] args) =>
    Log(string.Format(format, args), severity, unity);
  public void LogDebugFormat(bool unity, string format, params object[] args) =>
    LogFormat(unity, LogSeverity.Debug, format, args);
  public void LogInfoFormat(bool unity, string format, params object[] args) =>
    LogFormat(unity, LogSeverity.Information, format, args);
  public void LogWarningFormat(bool unity, string format, params object[] args) =>
    LogFormat(unity, LogSeverity.Warning, format, args);
  public void LogErrorFormat(bool unity, string format, params object[] args) =>
    LogFormat(unity, LogSeverity.Error, format, args);
  public void LogFatalFormat(bool unity, string format, params object[] args) =>
    LogFormat(unity, LogSeverity.Fatal, format, args);
}