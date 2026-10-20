namespace GitUi.Application.Abstractions;

/// <summary>
/// Which branches the graph shows, by full ref name ("refs/heads/main", "refs/remotes/origin/main").
/// Hidden branches drop out of the graph along with the commits only they reach; when any branch
/// is soloed, the graph shows only the soloed branches' history.
/// </summary>
public sealed record GraphRefFilter(IReadOnlyList<string> Hidden, IReadOnlyList<string> Solo)
{
    public static GraphRefFilter Empty { get; } = new(Array.Empty<string>(), Array.Empty<string>());

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Hidden.Count == 0 && Solo.Count == 0;

    public static string RefName(Domain.Entities.GitBranch branch) =>
        branch.IsRemote ? $"refs/remotes/{branch.Name}" : $"refs/heads/{branch.Name}";
}
