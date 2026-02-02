
using System;
using System.Globalization;
using System.Security.Cryptography;

namespace StationeersLaunchPad
{
  public static class DataUtils
  {
    private static readonly StringComparer Invariant =
      CultureInfo.InvariantCulture.CompareInfo.GetStringComparer(
        CompareOptions.OrdinalIgnoreCase);

    public unsafe static int CompareToInvariant(
      this ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
      if (left.Length == 0)
        return right.Length > 0 ? -1 : 0;
      if (right.Length == 0)
        return 1;
      fixed (char* lraw = &left[0], rraw = &right[0])
      {
        return Invariant.Compare(
          new string(lraw, 0, left.Length),
          new string(rraw, 0, right.Length)
        );
      }
    }

    public static string DigestSHA256(byte[] data)
    {
      const string hexChars = "0123456789abcdef";
      const string prefix = "sha256:";

      using var sha256 = SHA256.Create();
      var rawDigest = sha256.ComputeHash(data);
      var digest = new char[rawDigest.Length * 2 + prefix.Length];
      prefix.AsSpan().CopyTo(digest);
      for (var i = 0; i < rawDigest.Length; i++)
      {
        var b = rawDigest[i];
        var j = prefix.Length + i * 2;
        digest[j] = hexChars[(b >> 4) & 0xF];
        digest[j + 1] = hexChars[b & 0xF];
      }
      return new(digest);
    }
  }
}