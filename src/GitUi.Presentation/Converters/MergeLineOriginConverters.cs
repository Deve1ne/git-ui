using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GitUi.Application.Services;

namespace GitUi.Presentation.Converters;

/// <summary>
/// Output-line highlight in the merge tool, matching the pane tints: A (current) blue, B (incoming)
/// purple, unresolved conflicts orange, merged lines plain.
/// </summary>
public sealed class MergeLineOriginToBrushConverter : IValueConverter
{
    public static readonly MergeLineOriginToBrushConverter Instance = new();

    public static readonly IBrush OursBrush = new SolidColorBrush(Color.Parse("#334D88FF"));
    public static readonly IBrush TheirsBrush = new SolidColorBrush(Color.Parse("#33C517B6"));
    private static readonly IBrush UnresolvedBrush = new SolidColorBrush(Color.Parse("#40DE9B43"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        MergeLineOrigin.Ours => OursBrush,
        MergeLineOrigin.Theirs => TheirsBrush,
        MergeLineOrigin.Unresolved => UnresolvedBrush,
        _ => Brushes.Transparent,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Gutter marker of an output line: which side it was taken from.</summary>
public sealed class MergeLineOriginToMarkerConverter : IValueConverter
{
    public static readonly MergeLineOriginToMarkerConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        MergeLineOrigin.Ours => "A",
        MergeLineOrigin.Theirs => "B",
        MergeLineOrigin.Unresolved => "!",
        _ => string.Empty,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
