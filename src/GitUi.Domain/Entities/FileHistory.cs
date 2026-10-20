namespace GitUi.Domain.Entities;

/// <summary>A commit that changed a file; <see cref="Path"/> is the file's name in that commit (renames are followed).</summary>
public sealed record FileHistoryEntry(GitCommit Commit, string Path);

/// <summary>One line of a blamed file and the commit that last changed it.</summary>
/// <param name="StartsHunk">First line of a run of lines from the same commit: only it shows the annotation.</param>
public sealed record BlameLine(int LineNumber, string Content, string Sha, string Author, DateTimeOffset Date, string Summary, bool StartsHunk);
