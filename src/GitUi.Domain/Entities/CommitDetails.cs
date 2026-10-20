namespace GitUi.Domain.Entities;

/// <summary>A file changed by a commit, compared with its first parent.</summary>
/// <param name="OldPath">Previous path for a rename, otherwise null.</param>
public sealed record CommitFileChange(string Path, string? OldPath, FileChangeType ChangeType, int Additions, int Deletions);

/// <summary>Everything the commit detail panel shows about one commit.</summary>
public sealed record CommitDetails(
    string Sha,
    string Summary,
    string Body,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthorDate,
    string CommitterName,
    string CommitterEmail,
    DateTimeOffset CommitDate,
    IReadOnlyList<string> ParentShas,
    IReadOnlyList<CommitFileChange> Files)
{
    public int TotalAdditions => Files.Sum(f => f.Additions);

    public int TotalDeletions => Files.Sum(f => f.Deletions);
}
