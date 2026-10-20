using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Processes;
using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class GitignoreTests
{
    private readonly CliGitRepositoryWriter _writer = new(new GitCliRunner(new ProcessRunner(), NullLogger<GitCliRunner>.Instance));

    [Fact]
    public async Task AddToGitignoreAsync_AppendsOnceAndTheFileStopsShowingAsUntracked()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("a.txt", "a", "a");
        File.WriteAllText(Path.Combine(fixture.Path, ".gitignore"), "bin/"); // no final newline
        File.WriteAllText(Path.Combine(fixture.Path, "debug.log"), "noise");

        Assert.True(await _writer.AddToGitignoreAsync(fixture.Path, "*.log"));
        Assert.False(await _writer.AddToGitignoreAsync(fixture.Path, "*.log"));

        Assert.Equal("bin/\n*.log\n", File.ReadAllText(Path.Combine(fixture.Path, ".gitignore")));
        using var repo = new Repository(fixture.Path);
        Assert.True(repo.Ignore.IsPathIgnored("debug.log"));
    }
}
