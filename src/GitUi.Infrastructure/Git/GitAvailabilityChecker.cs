using GitUi.Infrastructure.Processes;

namespace GitUi.Infrastructure.Git;

public sealed class GitAvailabilityChecker
{
    private readonly IProcessRunner _processRunner;

    public GitAvailabilityChecker(IProcessRunner processRunner) => _processRunner = processRunner;

    public async Task<bool> IsGitAvailableAsync(CancellationToken cancellationToken = default)
    {
        var result = await _processRunner.RunAsync("git", new[] { "--version" }, Directory.GetCurrentDirectory(), TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0;
    }
}
