using GitUi.Application.Abstractions;
using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Processes;
using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class CommitOptionsTests
{
    private readonly CliGitRepositoryWriter _writer = new(new GitCliRunner(new ProcessRunner(), NullLogger<GitCliRunner>.Instance));

    [Fact]
    public async Task CommitAsync_Amend_ReplacesHeadKeepingItsParent()
    {
        using var fixture = new TestRepositoryFixture();
        var first = fixture.CommitFile("a.txt", "one", "first");
        fixture.CommitFile("a.txt", "two", "second");
        File.WriteAllText(Path.Combine(fixture.Path, "a.txt"), "two, fixed");
        await _writer.StageAsync(fixture.Path, "a.txt");

        await _writer.CommitAsync(fixture.Path, "second, amended", new GitCommitOptions(Amend: true));

        using var repo = new Repository(fixture.Path);
        Assert.Equal("second, amended", repo.Head.Tip.MessageShort);
        Assert.Equal(first, Assert.Single(repo.Head.Tip.Parents).Sha);
        Assert.Equal(2, repo.Commits.Count());
    }

    [Fact]
    public async Task CommitAsync_SkipHooks_BypassesFailingPreCommitHook()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("a.txt", "one", "first");
        var hooks = Path.Combine(fixture.Path, ".git", "hooks");
        Directory.CreateDirectory(hooks);
        File.WriteAllText(Path.Combine(hooks, "pre-commit"), "#!/bin/sh\nexit 1\n");
        using (var config = new Repository(fixture.Path))
        {
            // A global core.hooksPath would otherwise hide the repository's own hooks folder.
            config.Config.Set("core.hooksPath", hooks.Replace('\\', '/'), ConfigurationLevel.Local);
        }

        File.WriteAllText(Path.Combine(fixture.Path, "a.txt"), "two");
        await _writer.StageAsync(fixture.Path, "a.txt");

        await Assert.ThrowsAnyAsync<Exception>(() => _writer.CommitAsync(fixture.Path, "blocked"));
        await _writer.CommitAsync(fixture.Path, "skipped hooks", new GitCommitOptions(SkipHooks: true));

        using var repo = new Repository(fixture.Path);
        Assert.Equal("skipped hooks", repo.Head.Tip.MessageShort);
    }
}
