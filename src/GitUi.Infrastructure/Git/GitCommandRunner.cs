using GitUi.Application.Abstractions;
using GitUi.Infrastructure.Processes;

namespace GitUi.Infrastructure.Git;

public sealed class GitCommandRunner : IGitCommandRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);
    private readonly GitCliRunner _cli;
    private readonly IProcessRunner _processes;

    public GitCommandRunner(GitCliRunner cli, IProcessRunner processes)
    {
        _cli = cli;
        _processes = processes;
    }

    public Task<string> RunAsync(string path, IReadOnlyList<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => _cli.RunAsync(path, arguments, timeout ?? DefaultTimeout, cancellationToken);

    public async Task<GitCommandResult> TryRunAsync(string path, IReadOnlyList<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var result = await _processes.RunAsync("git", arguments, path, timeout ?? DefaultTimeout, cancellationToken);
        return new GitCommandResult(result.ExitCode, result.StandardOutput.Trim(), result.StandardError.Trim());
    }

    public Exception ToException(IReadOnlyList<string> arguments, GitCommandResult result) =>
        GitErrorClassifier.Classify("git " + string.Join(' ', arguments), new ProcessResult(result.ExitCode, result.Output, result.Error));
}
