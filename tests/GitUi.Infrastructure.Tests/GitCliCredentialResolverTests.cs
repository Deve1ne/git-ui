using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Processes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class GitCliCredentialResolverTests : IClassFixture<TestRepositoryFixture>
{
    private readonly TestRepositoryFixture _fixture;

    public GitCliCredentialResolverTests(TestRepositoryFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task EnsureCredentialHelperConfiguredAsync_NoHelperConfigured_DoesNotThrow()
    {
        var runner = new GitCliRunner(new ProcessRunner(), NullLogger<GitCliRunner>.Instance);
        var resolver = new GitCliCredentialResolver(runner, NullLogger<GitCliCredentialResolver>.Instance);

        await resolver.EnsureCredentialHelperConfiguredAsync(_fixture.Path);
    }
}
