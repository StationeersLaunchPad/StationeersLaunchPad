
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StationeersLaunchPad.Metadata;

// separate from WorkshopPackageCode so the SLP1 format stays stable
public static class Slp2PackageCode
{
  public readonly struct Entry(ulong workshopHandle, string name, string version)
  {
    public readonly ulong WorkshopHandle = workshopHandle;
    public readonly string Name = name;
    public readonly string Version = version;
  }

  private const string Prefix = "SLP2:";
  private const int MaxItems = 1000;
  private const byte ServerCodeFlag = 1;

  // the server name is embedded since LAN and direct connects aren't in the server list
  public static string Encode(IEnumerable<Entry> mods, bool serverCode, string serverName = null)
  {
    var list = new List<Entry>(mods);
    if (list.Count > MaxItems)
      return "";

    using var stream = new MemoryStream();
    stream.WriteByte(serverCode ? ServerCodeFlag : (byte)0);
    WriteVarString(stream, serverName ?? "");
    WriteVarUInt(stream, (ulong)list.Count);
    foreach (var mod in list)
    {
      WriteVarUInt(stream, mod.WorkshopHandle);
      WriteVarString(stream, mod.Name ?? "");
      WriteVarString(stream, mod.Version ?? "");
    }

    var payload = stream.ToArray();
    var checksum = GetChecksum(payload, payload.Length);
    stream.WriteByte((byte)checksum);
    stream.WriteByte((byte)(checksum >> 8));
    stream.WriteByte((byte)(checksum >> 16));
    stream.WriteByte((byte)(checksum >> 24));
    return Prefix + Convert.ToBase64String(stream.ToArray())
      .TrimEnd('=')
      .Replace('+', '-')
      .Replace('/', '_');
  }

  public static bool TryDecode(string code, out List<Entry> mods, out bool serverCode, out string serverName)
  {
    mods = [];
    serverCode = false;
    serverName = "";
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
    if (offset >= payloadLength)
      return false;
    serverCode = (bytes[offset++] & ServerCodeFlag) != 0;

    if (!TryReadVarString(bytes, ref offset, payloadLength, out serverName))
      return false;

    if (!TryReadVarUInt(bytes, ref offset, payloadLength, out var count)
      || count > MaxItems)
      return false;

    for (ulong i = 0; i < count; i++)
    {
      if (!TryReadVarUInt(bytes, ref offset, payloadLength, out var workshopHandle)
        || !TryReadVarString(bytes, ref offset, payloadLength, out var name)
        || !TryReadVarString(bytes, ref offset, payloadLength, out var version))
        return false;
      mods.Add(new(workshopHandle, name, version));
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

  private static void WriteVarString(Stream stream, string value)
  {
    var bytes = Encoding.UTF8.GetBytes(value);
    WriteVarUInt(stream, (ulong)bytes.Length);
    stream.Write(bytes, 0, bytes.Length);
  }

  private static bool TryReadVarUInt(byte[] bytes, ref int offset, int end, out ulong value)
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

  private static bool TryReadVarString(byte[] bytes, ref int offset, int end, out string value)
  {
    value = null;
    if (!TryReadVarUInt(bytes, ref offset, end, out var length))
      return false;
    if (offset < 0 || offset > end || length > (ulong)(end - offset))
      return false;
    var len = (int)length;
    value = Encoding.UTF8.GetString(bytes, offset, len);
    offset += len;
    return true;
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
