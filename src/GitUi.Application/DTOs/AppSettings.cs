namespace GitUi.Application.DTOs;

// Later additions are optional with null defaults so settings files written by older builds still load.
public sealed record AppSettings(
    IReadOnlyList<string> RecentRepositoryPaths,
    IReadOnlyList<string> PinnedRepositoryPaths,
    string Theme,
    IReadOnlyList<string>? OpenRepositoryPaths = null,
    string? ActiveRepositoryPath = null,
    LayoutSettings? Layout = null,
    string? PullMode = null,
    string? LastCloneDirectory = null,
    IReadOnlyDictionary<string, string>? ConflictTargetByRepository = null,
    IReadOnlyDictionary<string, Abstractions.GraphRefFilter>? GraphFilterByRepository = null,
    int? AutoFetchMinutes = null,
    bool? DiffSplitView = null,
    bool? DiffIgnoreWhitespace = null,
    double? Zoom = null,
    bool? FilesAsTree = null,
    IReadOnlyList<Hosting.HostingAccount>? HostingAccounts = null,
    IReadOnlyList<Services.GitProfile>? Profiles = null,
    string? ActiveProfileId = null,
    Abstractions.GitFetchOptions? FetchOptions = null)
{
    public static AppSettings CreateDefault() => new(Array.Empty<string>(), Array.Empty<string>(), "Dark");
}
