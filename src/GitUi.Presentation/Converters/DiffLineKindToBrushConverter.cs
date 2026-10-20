using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GitUi.Domain.ValueObjects;

namespace GitUi.Presentation.Converters;

/// <summary>Row background for a diff line: translucent tints so text stays readable in both themes.</summary>
public sealed class DiffLineKindToBrushConverter : IValueConverter
{
    public static readonly DiffLineKindToBrushConverter Instance = new();

    private static readonly IBrush AddedBackground = new SolidColorBrush(Color.Parse("#405CB85C"));
    private static readonly IBrush RemovedBackground = new SolidColorBrush(Color.Parse("#40D9413D"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DiffLineKind.Added => AddedBackground,
        DiffLineKind.Removed => RemovedBackground,
        _ => Brushes.Transparent,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Background of the changed words inside a modified line: a stronger tint of the row colour.</summary>
public sealed class DiffLineKindToHighlightBrushConverter : IValueConverter
{
    public static readonly DiffLineKindToHighlightBrushConverter Instance = new();

    private static readonly IBrush AddedHighlight = new SolidColorBrush(Color.Parse("#A05CB85C"));
    private static readonly IBrush RemovedHighlight = new SolidColorBrush(Color.Parse("#A0D9413D"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DiffLineKind.Added => AddedHighlight,
        DiffLineKind.Removed => RemovedHighlight,
        _ => null,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>The +/- gutter marker the reader strips from each diff line.</summary>
public sealed class DiffLineKindToPrefixConverter : IValueConverter
{
    public static readonly DiffLineKindToPrefixConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DiffLineKind.Added => "+",
        DiffLineKind.Removed => "-",
        _ => " ",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
