using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using TTT.Models;
using TTT.Services;

namespace TTT.Converters;

public sealed class AddressToHexConverter : IValueConverter
{
    public static readonly AddressToHexConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is long addr ? $"0x{addr:X8}" : "0x00000000";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = (value as string ?? string.Empty).Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        return long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result) ? result : 0L;
    }
}

public sealed class LogLevelToColorConverter : IValueConverter
{
    public static readonly LogLevelToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is LogLevel level
            ? level switch
            {
                LogLevel.Error => Brushes.IndianRed,
                LogLevel.Warning => Brushes.DarkOrange,
                LogLevel.Debug => Brushes.Silver,
                _ => Brushes.White
            }
            : Brushes.White;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public sealed class SavedPointerChainStringConverter : IValueConverter
{
    public static readonly SavedPointerChainStringConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is SavedPointer pointer ? pointer.ToChainString() : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public sealed class LongToHexConverter : IValueConverter
{
    public static readonly LongToHexConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is long addr && addr != 0 ? $"0x{addr:X}" : "--";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = (value as string ?? string.Empty).Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        return long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result) ? result : 0L;
    }
}

public sealed class CurrentViewIsTypeConverter : IValueConverter
{
    public static readonly CurrentViewIsTypeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
            return false;

        var expected = parameter.ToString();
        return !string.IsNullOrWhiteSpace(expected) &&
               string.Equals(value.GetType().Name, expected, StringComparison.Ordinal);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}
