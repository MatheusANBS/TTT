// File: Models/ScanEnumItems.cs
// Static helpers that expose enum values as bindable arrays for ComboBox ItemsSource.

using System;
using TTT.Models;

namespace TTT.Models;

public static class ScanValueTypeItems
{
    public static readonly Array All = Enum.GetValues(typeof(ScanValueType));
}

public static class ScanTypeItems
{
    public static readonly Array All = Enum.GetValues(typeof(ScanType));
}

