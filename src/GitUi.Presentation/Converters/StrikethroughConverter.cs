using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GitUi.Presentation.Converters;

/// <summary>Strikes text through when the bound flag is true (a commit dropped from a rebase).</summary>
public static class StrikethroughConverter
{
    public static readonly IValueConverter Instance =
        new FuncValueConverter<bool, TextDecorationCollection?>(strike => strike ? TextDecorations.Strikethrough : null);
}
