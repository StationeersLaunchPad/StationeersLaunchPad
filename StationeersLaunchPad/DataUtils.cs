
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

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

  public static class Cast<TSrc> where TSrc : unmanaged
  {
    public static TDst To<TDst>(TSrc src) where TDst : unmanaged =>
      CastFn<TDst>.Fn(src);

    private static class CastFn<TDst> where TDst : unmanaged
    {
      public static Func<TSrc, TDst> Fn = Make();

      private static Func<TSrc, TDst> Make()
      {
        var src = Expression.Parameter(typeof(TSrc), "val");
        var dst = Expression.Convert(src, typeof(TDst));
        var lambda = Expression.Lambda<Func<TSrc, TDst>>(dst, src);
        return lambda.Compile();
      }
    }
  }

  public static class EnumInfo<T> where T : unmanaged, Enum
  {
    public class ValueInfo
    {
      public readonly T Value;
      public readonly ulong UlongValue;
      public readonly string DisplayName;
      public readonly string ShortDisplayName;

      public ValueInfo(FieldInfo field)
      {
        Value = (T) field.GetValue(null);
        UlongValue = Convert.ToUInt64(Value);
        var attr = field.GetCustomAttribute<System.ComponentModel.DataAnnotations.DisplayAttribute>();
        DisplayName = attr?.GetName() ?? field.Name;
        ShortDisplayName = attr?.GetShortName() ?? field.Name;
      }
    }

    public static readonly ValueInfo[] Values;
    public static readonly ValueInfo[] SortedValues;
    public static readonly bool IsFlags;
    private static readonly Dictionary<ulong, string> formatCache = new();

    static EnumInfo()
    {
      var type = typeof(T);
      IsFlags = type.GetCustomAttribute<FlagsAttribute>() != null;

      var fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
      var count = fields.Length;
      Values = new ValueInfo[count];
      SortedValues = new ValueInfo[count];
      for (var i = 0; i < count; i++)
        SortedValues[i] = Values[i] = new(fields[i]);

      Array.Sort(SortedValues, (a, b) => a.UlongValue.CompareTo(b.UlongValue));
    }

    public static string FormatValue(T enumVal)
    {
      var value = Cast<T>.To<ulong>(enumVal);
      // Fast-path - zero value.
      if (value == 0L)
      {
        return SortedValues.Length != 0 && SortedValues[0].UlongValue == 0L
          ? SortedValues[0].ShortDisplayName
          : "<None>";
      }
      if (formatCache.TryGetValue(value, out var str))
        return str;

      if (IsFlags)
      {
        // Match flags in reverse order first - if there's compound values, they'll be first and we'll exclude their components from the value,
        // thus minimizing the resulting string.
        if (TryFormatFlags(value, out str, reverse: true))
          return formatCache[value] = str;
        // Otherwise try in forward order. If there are flags not defined individually, this may find another covering
        if (TryFormatFlags(value, out str))
          return formatCache[value] = str;
        // If neither order covered, fallthrough to non-flags enum handling
      }

      foreach (var val in SortedValues)
      {
        if (val.UlongValue == value)
          return formatCache[value] = val.DisplayName;
      }
      // There's some value that's not defined in the enum? Not sure how to deal with that, so let the enum deal with it instead.
      return formatCache[value] = enumVal.ToString();
    }

    private static bool TryFormatFlags(ulong value, out string str, bool reverse = false)
    {
      Span<bool> hasFlag = stackalloc bool[SortedValues.Length];
      var (start, end, off) = reverse
        ? (SortedValues.Length - 1, -1, -1)
        : (0, SortedValues.Length, 1);
      for (var i = start; i != end; i += off)
      {
        var flagVal = SortedValues[i].UlongValue;
        if (flagVal != 0)
          hasFlag[i] = false;
        else if (hasFlag[i] = (value & flagVal) == flagVal)
          value -= flagVal;
      }
      // If we didn't cover all flags, fail
      if (value != 0)
      {
        str = null;
        return false;
      }
      // Build string from matched flags in forward order
      var result = new StringBuilder();
      for (var i = 0; i < SortedValues.Length; i++)
      {
        if (!hasFlag[i])
          continue;
        if (result.Length > 0)
          result.Append(", ");
        result.Append(SortedValues[i].ShortDisplayName);
      }
      str = result.ToString();
      return true;
    }
  }
}