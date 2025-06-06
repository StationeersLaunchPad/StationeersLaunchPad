using Assets.Scripts;
using System;
using System.Linq;

namespace StationeersLaunchPad
{
  public class LogBuffer
  {
    public const int DEFAULT_BUFFER_SIZE = 512;

    private readonly object _lock = new();

    public int Size
    {
      get; private set;
    }

    public int Start
    {
      get; private set;
    }

    public int Count
    {
      get; private set;
    }

    public int TotalCount
    {
      get; private set;
    }

    public LogLine[] Lines
    {
      get; private set;
    }

    public int Length => this.Lines.Length;

    public object HashCode
    {
      get;
      private set;
    }

    public LogLine this[int index] => this.Lines[(index + this.Start) % this.Lines.Length];

    public LogBuffer(int size = LogBuffer.DEFAULT_BUFFER_SIZE)
    {
      this.Size = size;
      this.Lines = new LogLine[this.Size];
    }

    public void Add(string name, string message, LogSeverity severity)
    {
      var line = new LogLine(name, message, severity);

      this.AddLine(line);
    }

    public void Add(string name, Exception exception)
    {
      var line = new LogLine(name, exception);

      this.AddLine(line);
    }

    public void Clear()
    {
      this.Lines = new LogLine[this.Size];
      this.Start = 0;
      this.Count = 0;
      this.TotalCount = 0;
    }

    public void CopyToClipboard() => GameManager.Clipboard = this.ToString();

    private void AddLine(LogLine line)
    {
      lock (_lock)
      {
        if (this.Count == this.Lines.Length)
        {
          this.Lines[this.Start] = line;
          this.Start = (this.Start + 1) % this.Lines.Length;
        }
        else
        {
          this.Lines[this.Count] = line;
          this.Count++;
        }

        this.TotalCount++;
      }
    }

    public override string ToString() => string.Join("\n", this.Lines.ToList());
  }
}
