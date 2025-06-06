using System;
using UnityEngine;

namespace StationeersLaunchPad
{
  public class Logger
  {
    public static Logger Global
    {
      get; private set;
    } = new Logger("Global");

    public string Name
    {
      get; private set;
    }

    public Logger Parent
    {
      get; private set;
    }

    public LogBuffer Buffer
    {
      get; private set;
    }

    public bool IsGlobal => this.Parent == null;

    public bool IsChild => this.Parent != null;

    public Logger(string name = "", Logger parent = null)
    {
      this.Name = name;
      this.Parent = parent;
      this.Buffer = new LogBuffer(this.IsGlobal ? LogBuffer.DEFAULT_BUFFER_SIZE : 256);
    }

    public Logger(string name, LogBuffer buffer, Logger parent = null)
    {
      this.Name = name;
      this.Parent = parent;
      this.Buffer = buffer;
    }

    public Logger CreateChild(string name) => new Logger(name, this);

    public void CopyToClipboard() => this.Buffer.CopyToClipboard();

    public void Clear() => this.Buffer.Clear();

    public void Log(string message, LogSeverity logSeverity = LogSeverity.Information, bool unity = true, string name = "")
    {
      this.Buffer.Add(string.IsNullOrWhiteSpace(name) ? this.Name : name, message, logSeverity);

      if (this.IsChild)
        this.Parent?.Log(message, logSeverity, unity, this.Name);
      else if (unity)
        this.LogUnity(message, logSeverity);
    }

    public void Log(Exception exception, bool unity = true, string name = "")
    {
      this.Buffer.Add(string.IsNullOrWhiteSpace(name) ? this.Name : name, exception);

      if (this.IsChild)
        this.Parent?.Log(exception, unity, this.Name);
      else if (unity)
        this.LogUnity(exception);
    }

    public void LogUnity(string message, LogSeverity severity = LogSeverity.Information, string name = "")
    {
      switch (severity)
      {
        default:
        case LogSeverity.Debug:
        case LogSeverity.Information:
          this.LogUnity(message, LogType.Log, name);
          break;
        case LogSeverity.Warning:
          this.LogUnity(message, LogType.Warning, name);
          break;
        case LogSeverity.Error:
        case LogSeverity.Fatal:
          this.LogUnity(message, LogType.Error, name);
          break;
        case LogSeverity.Exception:
          this.LogUnity(message, LogType.Exception, name);
          break;
      }
    }

    public void LogUnity(string message, LogType logType = LogType.Log, string name = "") => Debug.LogFormat(logType, LogOption.None, null, $"[{(string.IsNullOrWhiteSpace(name) ? this.Name : name)}]: {message}");
    public void LogUnity(Exception exception) => Debug.LogException(exception);
    public void LogUnityAssert(string message) => this.LogUnity(message, LogType.Assert);
    public void LogUnityWarning(string message) => this.LogUnity(message, LogType.Warning);
    public void LogUnityError(string message) => this.LogUnity(message, LogType.Error);
    public void LogUnityException(Exception exception) => this.LogUnity(exception);

    public void LogDebug(string message, bool unity = true)
    {
      if (LaunchPadConfig.Debug)
      {
        this.Log(message, LogSeverity.Debug, unity);
      }
    }
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
