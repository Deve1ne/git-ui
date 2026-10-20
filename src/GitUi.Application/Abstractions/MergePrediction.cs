namespace GitUi.Application.Abstractions;

/// <summary>
/// Result of merging two revisions in memory (nothing is written to the repository or the
/// working directory): whether git would stop on conflicts, and in which files.
/// </summary>
public sealed record MergePrediction(bool HasConflicts, IReadOnlyList<string> ConflictedFiles)
{
    public static MergePrediction Clean { get; } = new(false, Array.Empty<string>());
}
