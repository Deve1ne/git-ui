using GitUi.Infrastructure.Processes;
using Microsoft.Extensions.Logging;

namespace GitUi.Infrastructure.Git;

public sealed class GitCliRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<GitCliRunner> _logger;

    public GitCliRunner(IProcessRunner processRunner, ILogger<GitCliRunner> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    /// <summary>Network operations (clone, fetch, pull, push) get this instead of the 30-second default.</summary>
    public static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(30);

    public Task<string> RunAsync(string repositoryPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        => RunAsync(repositoryPath, arguments, DefaultTimeout, cancellationToken);

    public async Task<string> RunAsync(string repositoryPath, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var displayCommand = "git " + string.Join(' ', arguments);
        _logger.LogDebug("Running {Command} in {Path}", displayCommand, repositoryPath);

        var result = await _processRunner.RunAsync("git", arguments, repositoryPath, timeout, cancellationToken);

        if (result.ExitCode != 0)
        {
            var exception = GitErrorClassifier.Classify(displayCommand, result);
            _logger.LogError(exception, "{Command} failed with exit code {ExitCode}", displayCommand, result.ExitCode);
            throw exception;
        }

        return result.StandardOutput.Trim();
    }
}
