using System;
using System.Collections.Generic;
using System.IO;

namespace StationeersLaunchPad.Metadata;

public static class WorkshopPackageCode
{
  private const string Prefix = "SLP1:";
  private const int MaxItems = 1000;

  public static string Encode(IEnumerable<ulong> workshopIds)
  {
    var ids = new List<ulong>(workshopIds);
    if (ids.Count == 0 || ids.Count > MaxItems)
      return "";

    var unique = new HashSet<ulong>();
    using var stream = new MemoryStream();
    WriteVarUInt(stream, (ulong)ids.Count);
    foreach (var id in ids)
    {
      if (id < 2 || !unique.Add(id))
        return "";
      WriteVarUInt(stream, id);
    }

    var payload = stream.ToArray();
    var checksum = GetChecksum(payload, payload.Length);
    stream.WriteByte((byte)checksum);
    stream.WriteByte((byte)(checksum >> 8));
    stream.WriteByte((byte)(checksum >> 16));
    stream.WriteByte((byte)(checksum >> 24));
    return Prefix + Convert.ToBase64String(stream.ToArray())
      .TrimEnd(new[] { '=' })
      .Replace('+', '-')
      .Replace('/', '_');
  }

  public static bool TryDecode(string code, out List<ulong> workshopIds)
  {
    workshopIds = [];
    if (string.IsNullOrWhiteSpace(code))
      return false;

    code = code.Trim();
    if (!code.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
      return false;

    byte[] bytes;
    try
    {
      var encoded = code[Prefix.Length..].Replace('-', '+').Replace('_', '/');
      if (encoded.Length % 4 == 1)
        return false;
      encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
      bytes = Convert.FromBase64String(encoded);
    }
    catch (FormatException)
    {
      return false;
    }

    if (bytes.Length < 6)
      return false;

    var payloadLength = bytes.Length - 4;
    var checksum = (uint)(bytes[payloadLength]
      | bytes[payloadLength + 1] << 8
      | bytes[payloadLength + 2] << 16
      | bytes[payloadLength + 3] << 24);
    if (checksum != GetChecksum(bytes, payloadLength))
      return false;

    var offset = 0;
    if (!TryReadVarUInt(bytes, ref offset, payloadLength, out var count)
      || count == 0 || count > MaxItems)
      return false;

    var unique = new HashSet<ulong>();
    for (ulong i = 0; i < count; i++)
    {
      if (!TryReadVarUInt(bytes, ref offset, payloadLength, out var id)
        || id < 2 || !unique.Add(id))
        return false;
      workshopIds.Add(id);
    }
    return offset == payloadLength;
  }

  private static void WriteVarUInt(Stream stream, ulong value)
  {
    while (value >= 0x80)
    {
      stream.WriteByte((byte)(value | 0x80));
      value >>= 7;
    }
    stream.WriteByte((byte)value);
  }

  private static bool TryReadVarUInt(
    byte[] bytes, ref int offset, int end, out ulong value)
  {
    value = 0;
    for (var shift = 0; shift < 64; shift += 7)
    {
      if (offset >= end)
        return false;
      var next = bytes[offset++];
      if (shift == 63 && (next & 0xfe) != 0)
        return false;
      value |= (ulong)(next & 0x7f) << shift;
      if ((next & 0x80) == 0)
        return true;
    }
    return false;
  }

  private static uint GetChecksum(byte[] bytes, int length)
  {
    var hash = 2166136261u;
    for (var i = 0; i < length; i++)
    {
      hash ^= bytes[i];
      hash *= 16777619;
    }
    return hash;
  }
}
