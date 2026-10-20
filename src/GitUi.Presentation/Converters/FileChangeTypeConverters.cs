using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GitUi.Domain.Entities;

namespace GitUi.Presentation.Converters;

/// <summary>One-letter status marker (A/M/D/R) shown before a file in the staging lists.</summary>
public sealed class FileChangeTypeToLetterConverter : IValueConverter
{
    public static readonly FileChangeTypeToLetterConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        FileChangeType.Added => "A",
        FileChangeType.Modified => "M",
        FileChangeType.Deleted => "D",
        FileChangeType.Renamed => "R",
        FileChangeType.Conflicted => "C",
        _ => "?",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class FileChangeTypeToBrushConverter : IValueConverter
{
    public static readonly FileChangeTypeToBrushConverter Instance = new();

    private static readonly IBrush Added = new SolidColorBrush(Color.Parse("#5CB85C"));
    private static readonly IBrush Modified = new SolidColorBrush(Color.Parse("#DE9B43"));
    private static readonly IBrush Deleted = new SolidColorBrush(Color.Parse("#D9413D"));
    private static readonly IBrush Renamed = new SolidColorBrush(Color.Parse("#4D88FF"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        FileChangeType.Added => Added,
        FileChangeType.Modified => Modified,
        FileChangeType.Deleted => Deleted,
        FileChangeType.Renamed => Renamed,
        FileChangeType.Conflicted => Deleted,
        _ => Brushes.Gray,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Label of the per-row stage toggle: a staged file offers "Unstage", an unstaged one "Stage".</summary>
public sealed class BoolToStageLabelConverter : IValueConverter
{
    public static readonly BoolToStageLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "Unstage" : "Stage";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Which side of the index a working-directory diff compares.</summary>
public sealed class BoolToDiffSourceConverter : IValueConverter
{
    public static readonly BoolToDiffSourceConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "STAGED" : "UNSTAGED";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
