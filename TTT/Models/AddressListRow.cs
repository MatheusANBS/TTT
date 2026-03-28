// File: Models/AddressListRow.cs

namespace TTT.Models;

/// <summary>
/// Represents a row in the Address List view. A row can be either a group header
/// (when <see cref="IsGroup"/> is <see langword="true"/>) or a regular entry row
/// wrapping a <see cref="MemoryEntry"/>.
/// </summary>
public sealed class AddressListRow
{
    /// <summary>
    /// <see langword="true"/> if this row is a collapsible group header;
    /// <see langword="false"/> if it wraps a <see cref="MemoryEntry"/>.
    /// </summary>
    public bool IsGroup { get; init; }

    /// <summary>Display name for the group (only meaningful when <see cref="IsGroup"/> is <see langword="true"/>).</summary>
    public string? GroupName { get; init; }

    /// <summary>The underlying memory entry (only meaningful when <see cref="IsGroup"/> is <see langword="false"/>).</summary>
    public MemoryEntry? Entry { get; init; }

    /// <summary>Creates a group-header row.</summary>
    public static AddressListRow ForGroup(string groupName) =>
        new() { IsGroup = true, GroupName = groupName };

    /// <summary>Creates an entry row wrapping <paramref name="entry"/>.</summary>
    public static AddressListRow ForEntry(MemoryEntry entry) =>
        new() { IsGroup = false, Entry = entry };
}

