namespace GitUi.Domain.ValueObjects;

public enum DiffLineKind { Context, Added, Removed }

/// <param name="NoNewlineAtEnd">The line is the last of its file and has no line terminator ("\ No newline at end of file").</param>
public sealed record DiffLine(DiffLineKind Kind, string Content, bool NoNewlineAtEnd = false);
