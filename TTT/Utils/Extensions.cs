// File: Utils\Extensions.cs

using TTT.Models;
using System.Text;

namespace TTT.Utils;

/// <summary>
/// Extension methods for address formatting, byte conversion and value parsing.
/// </summary>
public static class Extensions
{
    // ── Address / hex helpers ─────────────────────────────────────────────

    /// <summary>Formats a 64-bit address as an uppercase hex string with "0x" prefix.</summary>
    public static string ToHexString(this long address) =>
        $"0x{address:X16}".TrimStart('0').PadLeft(3).Replace("0x0", "0x");

    /// <summary>Formats a 64-bit address as compact uppercase hex (strips leading zeros, keeps min 1).</summary>
    public static string ToHexCompact(this long address) =>
        string.IsNullOrEmpty(address.ToString("X")) ? "0x0" : $"0x{address:X}";

    /// <summary>
    /// Attempts to parse a hex string (with or without "0x" prefix) to a <see cref="long"/>.
    /// Returns <see langword="null"/> on failure.
    /// </summary>
    public static long? ParseHex(this string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var clean = input.Trim().TrimStart('0', 'x').TrimStart('0', 'X');
        if (clean.StartsWith("x", StringComparison.OrdinalIgnoreCase))
            clean = clean[1..];
        if (string.IsNullOrEmpty(clean)) return 0L;
        return long.TryParse(clean, System.Globalization.NumberStyles.HexNumber, null, out var val) ? val : null;
    }

    // ── Byte array ↔ typed value ──────────────────────────────────────────

    /// <summary>Converts a typed value string to its binary representation for the given <see cref="ScanValueType"/>.</summary>
    /// <param name="value">String representation of the value to encode.</param>
    /// <param name="type">Target scan type.</param>
    /// <returns>Byte array or <see langword="null"/> if parsing fails.</returns>
    public static byte[]? ToBytes(this string value, ScanValueType type) =>
        type switch
        {
            ScanValueType.Byte1   => byte.TryParse(value, out var b) ? [b] : null,
            ScanValueType.Byte2   => short.TryParse(value, out var s) ? BitConverter.GetBytes(s) : null,
            ScanValueType.Byte4   => int.TryParse(value, out var i) ? BitConverter.GetBytes(i) : null,
            ScanValueType.Byte8   => long.TryParse(value, out var l) ? BitConverter.GetBytes(l) : null,
            ScanValueType.Float   => float.TryParse(value, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out var f)
                                     ? BitConverter.GetBytes(f) : null,
            ScanValueType.Double  => double.TryParse(value, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out var d)
                                     ? BitConverter.GetBytes(d) : null,
            ScanValueType.String  => Encoding.Unicode.GetBytes(value),
            _ => null
        };

    /// <summary>Returns the byte count for a given <see cref="ScanValueType"/> (0 for string = variable).</summary>
    public static int ByteSize(this ScanValueType type) =>
        type switch
        {
            ScanValueType.Byte1  => 1,
            ScanValueType.Byte2  => 2,
            ScanValueType.Byte4  => 4,
            ScanValueType.Byte8  => 8,
            ScanValueType.Float  => 4,
            ScanValueType.Double => 8,
            ScanValueType.String => 0,
            _ => 4
        };

    /// <summary>Converts raw bytes from memory into a display string for the given <see cref="ScanValueType"/>.</summary>
    public static string ReadValueAs(this byte[] raw, ScanValueType type) =>
        type switch
        {
            ScanValueType.Byte1  when raw.Length >= 1 => raw[0].ToString(),
            ScanValueType.Byte2  when raw.Length >= 2 => BitConverter.ToInt16(raw, 0).ToString(),
            ScanValueType.Byte4  when raw.Length >= 4 => BitConverter.ToInt32(raw, 0).ToString(),
            ScanValueType.Byte8  when raw.Length >= 8 => BitConverter.ToInt64(raw, 0).ToString(),
            ScanValueType.Float  when raw.Length >= 4 => BitConverter.ToSingle(raw, 0)
                                                            .ToString("G", System.Globalization.CultureInfo.InvariantCulture),
            ScanValueType.Double when raw.Length >= 8 => BitConverter.ToDouble(raw, 0)
                                                            .ToString("G", System.Globalization.CultureInfo.InvariantCulture),
            ScanValueType.String => Encoding.Unicode.GetString(raw).TrimEnd('\0'),
            _ => "??"
        };

    // ── Byte comparison helpers ───────────────────────────────────────────

    /// <summary>Returns <see langword="true"/> if two byte spans are element-wise equal.</summary>
    public static bool ByteSequenceEqual(this byte[] a, byte[] b)
        => a.AsSpan().SequenceEqual(b);

    /// <summary>Interprets a byte array as a <see cref="long"/> value (little-endian, sign-extended).</summary>
    public static long ToLongValue(this byte[] raw, ScanValueType type) =>
        type switch
        {
            ScanValueType.Byte1  when raw.Length >= 1 => raw[0],
            ScanValueType.Byte2  when raw.Length >= 2 => BitConverter.ToInt16(raw, 0),
            ScanValueType.Byte4  when raw.Length >= 4 => BitConverter.ToInt32(raw, 0),
            ScanValueType.Byte8  when raw.Length >= 8 => BitConverter.ToInt64(raw, 0),
            ScanValueType.Float  when raw.Length >= 4 => (long)BitConverter.ToSingle(raw, 0),
            ScanValueType.Double when raw.Length >= 8 => (long)BitConverter.ToDouble(raw, 0),
            _ => 0L
        };
}

