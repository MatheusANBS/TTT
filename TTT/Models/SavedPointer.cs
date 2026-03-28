// File: Models\SavedPointer.cs

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TTT.Models;

/// <summary>A single visual token in the pointer chain hop display.</summary>
/// <param name="Text">The text to show (e.g. "game.exe+0x1A2B3C" or "+0x10").</param>
/// <param name="IsArrow">When <see langword="true"/> this token is a separator arrow, not a chip.</param>
public record ChainStep(string Text, bool IsArrow = false);

/// <summary>
/// A resolved pointer chain saved to the config file.
/// Describes the path from a static module base through a series of offsets to reach a target address.
/// Example display: <c>game.exe+0x1A2B3C → +0x10 → +0x8</c>
/// </summary>
public sealed class SavedPointer : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Absolute address of the static base (usually inside the main module or another PE image).</summary>
    public long ModuleBase { get; set; }

    /// <summary>Name of the module the base belongs to (e.g. "game.exe", "mono.dll").</summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>Offset from the module image base to the first pointer (ModuleBase - ImageBase).</summary>
    public long ModuleOffset { get; set; }

    /// <summary>
    /// Ordered list of pointer dereference offsets.
    /// Each value is added to the pointer read at the previous step.
    /// </summary>
    public List<long> Offsets { get; set; } = [];

    /// <summary>User-supplied label shown in the Address List and Pointer Mapper.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Data type at the final dereferenced address.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ScanValueType ValueType { get; set; } = ScanValueType.Byte4;

    private string _currentValue = "??";
    /// <summary>The last read value at the resolved pointer chain address (updated live).</summary>
    [JsonIgnore]
    public string CurrentValue
    {
        get => _currentValue;
        set { if (_currentValue != value) { _currentValue = value; Notify(); } }
    }

    private long _resolvedAddress;
    /// <summary>The final resolved address after walking the pointer chain.</summary>
    [JsonIgnore]
    public long ResolvedAddress
    {
        get => _resolvedAddress;
        set { if (_resolvedAddress != value) { _resolvedAddress = value; Notify(); } }
    }

    private bool _isVerified;
    /// <summary>True if the chain was confirmed to resolve to the expected target address post-scan.</summary>
    [JsonIgnore]
    public bool IsVerified
    {
        get => _isVerified;
        set { if (_isVerified != value) { _isVerified = value; Notify(); } }
    }

    /// <summary>
    /// Reliability score computed from chain length, module type and offset alignment.
    /// Higher is better. Use to sort/filter results.
    /// </summary>
    [JsonIgnore]
    public int Score { get; set; }

    /// <summary>
    /// Number of intermediate hops whose dereferenced pointer value lands in a stable
    /// (MEM_IMAGE or MEM_MAPPED) region. Hops through MEM_PRIVATE heap are unstable
    /// across game sessions.
    /// </summary>
    [JsonIgnore]
    public int StableHops
    {
        get => _stableHops;
        set { if (_stableHops != value) { _stableHops = value; Notify(); Notify(nameof(StabilityLabel)); } }
    }
    private int _stableHops;

    /// <summary>Total intermediate hops analysed for region stability.</summary>
    [JsonIgnore]
    public int TotalHops
    {
        get => _totalHops;
        set { if (_totalHops != value) { _totalHops = value; Notify(); Notify(nameof(StabilityLabel)); } }
    }
    private int _totalHops;

    /// <summary>
    /// Human-readable stability classification derived from <see cref="StableHops"/>/<see cref="TotalHops"/>.
    /// "Estático" = all hops through image/mapped → survives session changes.
    /// "Heap"    = every hop is a heap address → breaks on re-enter.
    /// "Misto"   = mixed → may or may not survive.
    /// </summary>
    [JsonIgnore]
    public string StabilityLabel => TotalHops == 0
        ? "Direto"
        : StableHops == TotalHops
            ? "Estático"
            : StableHops == 0
                ? "Dinâmico"
                : $"Misto ({StableHops}/{TotalHops})";

    /// <summary>
    /// Builds a human-readable chain string such as <c>game.exe+0x1A2B3C→+0x10→+0x8</c>.
    /// </summary>
    public string ToChainString()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(string.IsNullOrEmpty(ModuleName) ? $"0x{ModuleBase:X}" : $"{ModuleName}+0x{ModuleOffset:X}");
        foreach (var offset in Offsets)
        {
            if (offset < 0)
                sb.Append($" → -0x{-offset:X}");
            else
                sb.Append($" → +0x{offset:X}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Returns an alternating list of chip tokens and arrow separators used by the
    /// row-details hop visualisation in <c>PointerMapperView</c>.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<ChainStep> ChainSteps
    {
        get
        {
            var steps = new List<ChainStep>
            {
                new(string.IsNullOrEmpty(ModuleName) ? $"0x{ModuleBase:X}" : $"{ModuleName}+0x{ModuleOffset:X}")
            };
            foreach (var offset in Offsets)
            {
                steps.Add(new ChainStep("→", IsArrow: true));
                steps.Add(new ChainStep(offset < 0 ? $"-0x{-offset:X}" : $"+0x{offset:X}"));
            }
            return steps;
        }
    }
}

