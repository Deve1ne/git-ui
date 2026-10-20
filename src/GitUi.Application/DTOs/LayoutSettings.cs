namespace GitUi.Application.DTOs;

/// <summary>Panel and graph-column widths restored at startup. Column keys are column names.</summary>
/// <param name="HiddenGraphColumns">Graph columns the user turned off (column names).</param>
public sealed record LayoutSettings(
    double RefPanelWidth,
    double DetailPanelWidth,
    IReadOnlyDictionary<string, float> GraphColumnWidths,
    IReadOnlyList<string>? HiddenGraphColumns = null);
