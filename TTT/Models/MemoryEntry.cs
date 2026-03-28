// File: Models\MemoryEntry.cs

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TTT.Models;

/// <summary>
/// A row in the Address List. Can represent a static address or a pointer chain.
/// When <see cref="Offsets"/> is non-empty, the effective address is resolved live
/// by following the pointer chain each refresh cycle.
/// </summary>
public sealed class MemoryEntry : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private string _description = "No description";
    /// <summary>User-supplied description label (editable in the grid).</summary>
    public string Description
    {
        get => _description;
        set { if (_description != value) { _description = value; Notify(); } }
    }

    /// <summary>
    /// Base address. For static entries this is the final target address.
    /// For pointer-chain entries this is the module-relative base before applying <see cref="Offsets"/>.
    /// </summary>
    public long Address { get; set; }

    /// <summary>Data type of the value at the resolved address.</summary>
    public ScanValueType Type { get; set; } = ScanValueType.Byte4;

    private bool _isFrozen;
    /// <summary>When <see langword="true"/>, the timer writes <see cref="FrozenValue"/> back to memory every refresh.</summary>
    public bool IsFrozen
    {
        get => _isFrozen;
        set { if (_isFrozen != value) { _isFrozen = value; Notify(); } }
    }

    /// <summary>The byte array written back when <see cref="IsFrozen"/> is active.</summary>
    public byte[]? FrozenValue { get; set; }

    /// <summary>
    /// Pointer chain offsets. Empty for static addresses.
    /// Example: [0x10, 0x8] means: read pointer at <see cref="Address"/>, add 0x10, read again, add 0x8 → final address.
    /// </summary>
    public List<long> Offsets { get; set; } = [];

    private string _currentValue = "??";
    /// <summary>The last successfully read display value (refreshed by the 200 ms timer).</summary>
    public string CurrentValue
    {
        get => _currentValue;
        set { if (_currentValue != value) { _currentValue = value; Notify(); } }
    }

    private long _resolvedAddress;
    /// <summary>The last successfully resolved effective address (updated each tick when pointer chain is active).</summary>
    public long ResolvedAddress
    {
        get => _resolvedAddress;
        set { if (_resolvedAddress != value) { _resolvedAddress = value; Notify(); } }
    }

    /// <summary>Returns <see langword="true"/> if this entry uses a pointer chain.</summary>
    public bool IsPointerChain => Offsets.Count > 0;

    private string _groupName = "Geral";
    /// <summary>Group label used to visually cluster entries in the Address List grid.</summary>
    public string GroupName
    {
        get => _groupName;
        set { if (_groupName != value) { _groupName = value; Notify(); } }
    }
}

