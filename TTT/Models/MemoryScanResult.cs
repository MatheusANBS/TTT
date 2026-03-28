// File: Models\MemoryScanResult.cs

using CommunityToolkit.Mvvm.ComponentModel;

namespace TTT.Models;

/// <summary>
/// Represents a single address found during a memory scan.
/// Display values can be refreshed live while the row remains visible.
/// </summary>
public sealed partial class MemoryScanResult : ObservableObject
{
    /// <summary>Absolute virtual address in the target process.</summary>
    public required long Address { get; init; }

    /// <summary>Raw bytes read from memory at this address.</summary>
    [ObservableProperty] private byte[] _rawBytes = [];

    /// <summary>Human-readable formatted value (e.g. "1234", "3.14", "Hello").</summary>
    [ObservableProperty] private string _displayValue = string.Empty;

    /// <summary>The data type that was used during the scan.</summary>
    public required ScanValueType Type { get; init; }

    /// <summary>Snapshot bytes from the previous scan — used for relational comparisons (Increased, Changed, etc.).</summary>
    [ObservableProperty] private byte[]? _previousRawBytes;
}
