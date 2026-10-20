using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Processes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitUi.Infrastructure.Tests;

// Only the repository-local scope is exercised: tests must never touch the developer's global config.
public class GitConfigTests
{
    private readonly CliGitRepositoryWriter _writer = new(new GitCliRunner(new ProcessRunner(), NullLogger<GitCliRunner>.Instance));

    [Fact]
    public async Task LocalConfig_SetReadThenUnset()
    {
        using var fixture = new TestRepositoryFixture();

        await _writer.SetConfigValueAsync(fixture.Path, "gitui.test", "value one", global: false);
        Assert.Equal("value one", await _writer.GetConfigValueAsync(fixture.Path, "gitui.test", global: false));

        await _writer.SetConfigValueAsync(fixture.Path, "gitui.test", null, global: false);
        Assert.Null(await _writer.GetConfigValueAsync(fixture.Path, "gitui.test", global: false));
    }

    [Fact]
    public async Task UnsettingAMissingKey_DoesNotFail()
    {
        using var fixture = new TestRepositoryFixture();

        await _writer.SetConfigValueAsync(fixture.Path, "gitui.missing", "  ", global: false);

        Assert.Null(await _writer.GetConfigValueAsync(fixture.Path, "gitui.missing", global: false));
    }
}
