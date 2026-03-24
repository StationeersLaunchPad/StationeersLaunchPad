using System;
using System.Text;

namespace StationeersLaunchPad;

public class LogLine
{
  public readonly string Prefix;
  public readonly string Message;
  public readonly string Source;
  public readonly string StackTrace;
  public readonly LogSeverity Severity;

  public bool IsException => !string.IsNullOrEmpty(Source) || !string.IsNullOrEmpty(StackTrace);

  public readonly string FullString;
  public readonly string CompactString;

  public LogLine(string prefix, string message, LogSeverity severity)
  {
    Prefix = prefix;
    Message = message;
    Source = string.Empty;
    StackTrace = string.Empty;
    Severity = severity;

    FullString = $"[{Prefix} - {Severity}]: {Message}";
    CompactString = $"[{Prefix}]: {Message}";
  }

  public LogLine(string prefix, Exception exception)
  {
    Prefix = prefix;
    Message = exception.Message;
    Source = exception.Source ?? string.Empty;
    StackTrace = exception.StackTrace ?? string.Empty;
    Severity = LogSeverity.Exception;

    var sb = new StringBuilder();
    while (exception != null)
    {
      sb.AppendLine(exception.Message);
      sb.AppendLine(exception.StackTrace);
      exception = exception.InnerException;
    }
    var fullStackTrace = sb.ToString().Trim();

    FullString = $"[{Prefix} - {Source}]: {fullStackTrace}";
    CompactString = $"[{Prefix}]: {fullStackTrace}";
  }

  public override string ToString() => FullString;
}