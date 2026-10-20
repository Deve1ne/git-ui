using System.Globalization;
using Avalonia.Data.Converters;

namespace GitUi.Presentation.Converters;

/// <summary>A full SHA as the 7-character form shown everywhere in the UI.</summary>
public sealed class ShortShaConverter : IValueConverter
{
    public static readonly ShortShaConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string sha ? sha[..Math.Min(7, sha.Length)] : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
