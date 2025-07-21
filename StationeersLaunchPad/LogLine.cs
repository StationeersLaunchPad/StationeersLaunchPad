using System;
using System.Text;

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

    public readonly string FullString;
    public readonly string CompactString;

    public LogLine(string prefix, string message, LogSeverity severity)
    {
      this.Prefix = prefix;
      this.Message = message;
      this.Source = string.Empty;
      this.StackTrace = string.Empty;
      this.Severity = severity;

      this.FullString = $"[{this.Prefix} - {this.Severity}]: {this.Message}";
      this.CompactString = $"[{this.Prefix}]: {this.Message}";
    }

    public LogLine(string prefix, Exception exception)
    {
      this.Prefix = prefix;
      this.Message = exception.Message;
      this.Source = exception.Source ?? string.Empty;
      this.StackTrace = exception.StackTrace ?? string.Empty;
      this.Severity = LogSeverity.Exception;

      var sb = new StringBuilder();
      while (exception != null)
      {
        sb.AppendLine(exception.Message);
        sb.AppendLine(exception.StackTrace);
        exception = exception.InnerException;
      }
      var fullStackTrace = sb.ToString().Trim();

      this.FullString = $"[{this.Prefix} - {this.Source}]: {fullStackTrace}";
      this.CompactString = $"[{this.Prefix}]: {fullStackTrace}";
    }

    public override string ToString() => FullString;
  }
}
