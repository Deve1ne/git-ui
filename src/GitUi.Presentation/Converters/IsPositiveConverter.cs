using System.Globalization;
using Avalonia.Data.Converters;

namespace GitUi.Presentation.Converters;

/// <summary>True for a count above zero — hides "↑0" / "↓0" style badges.</summary>
public sealed class IsPositiveConverter : IValueConverter
{
    public static readonly IsPositiveConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
