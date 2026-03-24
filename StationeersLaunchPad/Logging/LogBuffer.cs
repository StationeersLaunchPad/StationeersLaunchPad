using System;
using System.Linq;
using Assets.Scripts;

namespace StationeersLaunchPad;

public class LogBuffer(int size = LogBuffer.DEFAULT_BUFFER_SIZE)
{
  public const int DEFAULT_BUFFER_SIZE = 512;

  private readonly object _lock = new();

  public readonly int Size = size;
  public readonly LogLine[] Lines = new LogLine[size];
  private int start;
  public int Count { get; private set; }
  public ulong TotalCount { get; private set; }

  public LogLine this[int index] => At(index);

  public LogLine At(int index)
  {
    lock (_lock)
    {
      return index >= 0 && index < Count ? Lines[(start + index) % Lines.Length] : null;
    }
  }

  public void Add(string name, string message, LogSeverity severity)
  {
    var line = new LogLine(name, message, severity);

    AddLine(line);
  }

  public void Add(string name, Exception exception)
  {
    var line = new LogLine(name, exception);

    AddLine(line);
  }

  public void Clear()
  {
    Array.Fill(Lines, null);
    start = 0;
    Count = 0;
    TotalCount = 0;
  }

  public void CopyToClipboard() => GameManager.Clipboard = ToString();

  private void AddLine(LogLine line)
  {
    lock (_lock)
    {
      if (Count < Size)
      {
        var index = (start + Count) % Size;
        Lines[index] = line;
        Count++;
      }
      else
      {
        Lines[start] = line;
        start = (start + 1) % Size;
      }

      TotalCount++;
    }
  }

  public override string ToString() => string.Join("\n", Enumerable.Range(0, Count).Select(At));
}