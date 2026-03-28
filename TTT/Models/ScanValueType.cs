// File: Models\ScanValueType.cs

namespace TTT.Models;

/// <summary>
/// Supported data types for memory scanning and the Address List.
/// </summary>
public enum ScanValueType
{
    /// <summary>Unsigned 8-bit integer (1 byte).</summary>
    Byte1,
    /// <summary>Signed 16-bit integer (2 bytes).</summary>
    Byte2,
    /// <summary>Signed 32-bit integer (4 bytes) — most common game value.</summary>
    Byte4,
    /// <summary>Signed 64-bit integer (8 bytes).</summary>
    Byte8,
    /// <summary>Single-precision floating point (4 bytes).</summary>
    Float,
    /// <summary>Double-precision floating point (8 bytes).</summary>
    Double,
    /// <summary>UTF-16 (Unicode) string — variable length.</summary>
    String
}

/// <summary>
/// Comparison condition used when scanning memory.
/// </summary>
public enum ScanType
{
    /// <summary>Find all locations with exactly the supplied value.</summary>
    ExactValue,
    /// <summary>First scan that records all values for subsequent comparison.</summary>
    UnknownInitialValue,
    /// <summary>Values greater than the previous scan snapshot.</summary>
    IncreasedValue,
    /// <summary>Values less than the previous scan snapshot.</summary>
    DecreasedValue,
    /// <summary>Any value different from the previous snapshot.</summary>
    ChangedValue,
    /// <summary>Values identical to the previous snapshot.</summary>
    UnchangedValue
}

