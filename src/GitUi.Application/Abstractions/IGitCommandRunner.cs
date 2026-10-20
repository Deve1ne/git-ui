namespace GitUi.Application.Abstractions;

/// <summary>The outcome of a git command that is allowed to fail (exit code checked by the caller).</summary>
public sealed record GitCommandResult(int ExitCode, string Output, string Error)
{
    public bool Success => ExitCode == 0;
}

/// <summary>
/// Runs any git command in a repository. The feature services (Git Flow, bisect, LFS, reflog,
/// signing) build on it instead of growing <see cref="IGitRepositoryWriter"/> for every subcommand.
/// </summary>
public interface IGitCommandRunner
{
    /// <summary>Runs git and returns its trimmed output; throws a <c>GitOperationException</c> on a non-zero exit.</summary>
    Task<string> RunAsync(string path, IReadOnlyList<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    /// <summary>Runs git and reports the exit code instead of throwing.</summary>
    Task<GitCommandResult> TryRunAsync(string path, IReadOnlyList<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    /// <summary>The exception <see cref="RunAsync"/> would have thrown for this failed result (authentication, network…).</summary>
    Exception ToException(IReadOnlyList<string> arguments, GitCommandResult result);
}
