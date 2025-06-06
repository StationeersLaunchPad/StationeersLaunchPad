using System;

namespace StationeersLaunchPad
{
  public class LogLine
  {
    public string Prefix
    {
      get; private set;
    }

    public string Message
    {
      get; private set;
    }

    public string Source
    {
      get; private set;
    }

    public string StackTrace
    {
      get; private set;
    }

    public LogSeverity Severity
    {
      get; private set;
    }

    public bool IsException => !string.IsNullOrEmpty(this.Source) || !string.IsNullOrEmpty(this.StackTrace);

    public LogLine(string prefix, string message, LogSeverity severity)
    {
      this.Prefix = prefix;
      this.Message = message;
      this.Source = string.Empty;
      this.StackTrace = string.Empty;
      this.Severity = severity;
    }

    public LogLine(string prefix, Exception exception)
    {
      this.Prefix = prefix;
      this.Message = exception.Message;
      this.Source = exception.Source ?? string.Empty;
      this.StackTrace = exception.StackTrace ?? string.Empty;
      this.Severity = LogSeverity.Exception;
    }

    public override string ToString() => this.IsException ? $"[{this.Prefix} - {this.Source} - {this.StackTrace}]: {this.Message}" : $"[{this.Prefix} - {this.Severity}]: {this.Message}";
  }
}
