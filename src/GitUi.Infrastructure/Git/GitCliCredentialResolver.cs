using GitUi.Application.Abstractions;
using GitUi.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace GitUi.Infrastructure.Git;

public sealed class GitCliCredentialResolver : ICredentialResolver
{
    private readonly GitCliRunner _runner;
    private readonly ILogger<GitCliCredentialResolver> _logger;

    public GitCliCredentialResolver(GitCliRunner runner, ILogger<GitCliCredentialResolver> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task EnsureCredentialHelperConfiguredAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var output = await _runner.RunAsync(repositoryPath, new[] { "config", "--get", "credential.helper" }, cancellationToken);
            if (string.IsNullOrWhiteSpace(output))
            {
                _logger.LogWarning("No git credential.helper configured for {Path}; remote authentication prompts may fail silently.", repositoryPath);
            }
        }
        catch (GitOperationException)
        {
            _logger.LogWarning("No git credential.helper configured for {Path}; remote authentication prompts may fail silently.", repositoryPath);
        }
    }
}
