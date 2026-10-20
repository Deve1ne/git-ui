namespace GitUi.Domain.Entities;

/// <param name="IsStash">A stash entry shown in the graph: <see cref="Sha"/> is its working-tree commit and its only parent is the commit it was made on.</param>
public sealed record GitCommit(string Sha, string Message, string Author, DateTimeOffset Date, IReadOnlyList<string> ParentShas, bool IsStash = false)
{
    public bool Equals(GitCommit? other) =>
        other is not null &&
        Sha == other.Sha &&
        Message == other.Message &&
        Author == other.Author &&
        Date == other.Date &&
        IsStash == other.IsStash &&
        ParentShas.SequenceEqual(other.ParentShas);

    public override int GetHashCode() =>
        HashCode.Combine(Sha, Message, Author, Date, ParentShas.Count, IsStash);
}
