// File: Models\PointerScanEntry.cs

using System.Text.Json.Serialization;

namespace TTT.Models;

/// <summary>
/// Lightweight, serialization-friendly representation of a pointer chain used
/// for multi-session comparison (.pscan files).
/// Only contains module-relative data — no runtime addresses — so chains from
/// different game sessions can be compared by identity.
/// Offsets are stored as hex strings ("0x20") for readability.
/// </summary>
public sealed class PointerScanEntry
{
    /// <summary>Module name the chain is anchored in (e.g. "FSD.exe").</summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>Offset from the module image base to the first pointer, as hex string ("0x1A2B3C").</summary>
    public string ModuleOffset { get; set; } = "0x0";

    /// <summary>Ordered dereference offsets as hex strings (e.g. ["+0x10", "-0x8"]).</summary>
    public List<string> Offsets { get; set; } = [];

    /// <summary>Reliability score at scan time (informational only, not used for comparison).</summary>
    public int Score { get; set; }

    /// <summary>User-facing label (informational only).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Number of intermediate hops classified as stable at scan time.
    /// Persisted to preserve stability labels after loading a .pscan file.
    /// </summary>
    public int StableHops { get; set; }

    /// <summary>
    /// Total intermediate hops analysed at scan time.
    /// Persisted to preserve stability labels after loading a .pscan file.
    /// </summary>
    public int TotalHops { get; set; }

    /// <summary>Value type at the resolved address.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ScanValueType ValueType { get; set; } = ScanValueType.Byte4;

    // ── Conversion helpers ─────────────────────────────────────────────

    public static string OffsetToHex(long offset) =>
        offset < 0 ? $"-0x{-offset:X}" : $"+0x{offset:X}";

    public static long HexToOffset(string s)
    {
        s = s.Trim();
        bool neg = s.StartsWith('-');
        s = s.TrimStart('+', '-').Replace("0x", "").Replace("0X", "");
        long v = long.Parse(s, System.Globalization.NumberStyles.HexNumber);
        return neg ? -v : v;
    }

    public long ModuleOffsetLong =>
        long.Parse(ModuleOffset.Replace("0x", "").Replace("0X", ""),
            System.Globalization.NumberStyles.HexNumber);

    public List<long> OffsetsLong => Offsets.Select(HexToOffset).ToList();
}

/// <summary>
/// Root envelope written to .pscan files.
/// Wraps the chain list with metadata so files are self-describing
/// and future format changes can be handled gracefully via <see cref="Version"/>.
/// </summary>
public sealed class PscanFile
{
    /// <summary>Format version. Current: 2.</summary>
    public int Version { get; set; } = 2;

    /// <summary>ISO-8601 timestamp when the file was saved.</summary>
    public string SavedAt { get; set; } = string.Empty;

    /// <summary>Hex address that was scanned for (e.g. "0x1A2B3C4D").</summary>
    public string TargetAddress { get; set; } = string.Empty;

    /// <summary>Maximum pointer depth used during the scan.</summary>
    public int MaxDepth { get; set; }

    /// <summary>Maximum offset used during the scan, as hex string.</summary>
    public string MaxOffset { get; set; } = string.Empty;

    /// <summary>The pointer chains found during the scan.</summary>
    public List<PointerScanEntry> Chains { get; set; } = [];
}

