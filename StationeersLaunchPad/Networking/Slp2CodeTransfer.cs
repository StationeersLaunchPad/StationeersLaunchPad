using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace StationeersLaunchPad.Networking;

// splits codes over the 1400 byte packet limit into frames for the probe channel
internal sealed class Slp2CodeTransfer
{
  internal const int MaxFramePayload = 1200;
  internal const int MaxCodeBytes = 128 * 1024;
  private const int HeaderBytes = 8;

  private byte[] buffer;
  private int received;
  private long sender;

  internal static IEnumerable<byte[]> Split(byte[] code)
  {
    for (var offset = 0; offset < code.Length; offset += MaxFramePayload - HeaderBytes)
    {
      var count = Math.Min(MaxFramePayload - HeaderBytes, code.Length - offset);
      var payload = new byte[HeaderBytes + count];
      BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), code.Length);
      BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), offset);
      code.AsSpan(offset, count).CopyTo(payload.AsSpan(HeaderBytes));
      yield return payload;
    }
  }

  internal bool TryAccept(long connectionId, ReadOnlySpan<byte> payload, out string code)
  {
    code = null;
    if (payload.Length <= HeaderBytes || payload.Length > MaxFramePayload)
    {
      Reset();
      return false;
    }
    var total = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0, 4));
    var offset = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4, 4));
    var count = payload.Length - HeaderBytes;
    if (total <= 0 || total > MaxCodeBytes || offset < 0 || offset > total
      || count > total - offset)
    {
      Reset();
      return false;
    }
    if (offset == 0)
    {
      buffer = new byte[total];
      received = 0;
      sender = connectionId;
    }
    if (buffer == null || sender != connectionId || buffer.Length != total
      || offset != received)
    {
      Reset();
      return false;
    }
    payload.Slice(HeaderBytes).CopyTo(buffer.AsSpan(received));
    received += count;
    if (received != total)
      return false;
    code = Encoding.UTF8.GetString(buffer);
    Reset();
    return true;
  }

  internal void Reset()
  {
    buffer = null;
    received = 0;
    sender = 0;
  }
}
