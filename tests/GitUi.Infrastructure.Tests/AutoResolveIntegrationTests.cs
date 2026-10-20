using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Processes;
using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class AutoResolveIntegrationTests
{
    private readonly CliGitRepositoryWriter _writer = new(new GitCliRunner(new ProcessRunner(), NullLogger<GitCliRunner>.Instance));
    private readonly ConflictResolutionService _service;

    public AutoResolveIntegrationTests() => _service = new ConflictResolutionService(new LibGit2SharpRepositoryReader(), _writer);

    /// <summary>main and "other" both change f.txt from "base"; returns after the merge stopped on the conflict.</summary>
    private async Task<TestRepositoryFixture> ConflictAsync(string baseText, string mainText, string otherText)
    {
        var fixture = new TestRepositoryFixture();
        using (var repo = new Repository(fixture.Path))
        {
            // Keep the bytes exactly as written: no CRLF conversion on this machine's settings.
            repo.Config.Set("core.autocrlf", "false", ConfigurationLevel.Local);
        }

        fixture.CommitFile("f.txt", baseText, "base");
        string main;
        using (var repo = new Repository(fixture.Path))
        {
            main = repo.Head.FriendlyName;
            repo.CreateBranch("other");
        }

        fixture.CommitFile("f.txt", mainText, "main change");
        using (var repo = new Repository(fixture.Path))
        {
            Commands.Checkout(repo, "other");
        }

        fixture.CommitFile("f.txt", otherText, "other change");
        using (var repo = new Repository(fixture.Path))
        {
            Commands.Checkout(repo, main);
        }

        await Assert.ThrowsAnyAsync<Exception>(() => _writer.MergeAsync(fixture.Path, "other", GitMergeMode.NoFastForward));
        using (var repo = new Repository(fixture.Path))
        {
            Assert.True(repo.Index.Conflicts.Any(), "the scenario must really conflict in git");
        }

        return fixture;
    }

    [Fact]
    public async Task LineEndingsChangedOnOneSide_GitConflicts_Diff3Resolves()
    {
        using var fixture = await ConflictAsync("one\ntwo\nthree\n", "one\r\ntwo\r\nthree\r\n", "one\ntwo\nTHREE\n");

        Assert.True(await _service.TryAutoResolveAsync(fixture.Path, "f.txt"));

        Assert.Equal("one\r\ntwo\r\nTHREE\r\n", File.ReadAllText(Path.Combine(fixture.Path, "f.txt")));
        using var repo = new Repository(fixture.Path);
        Assert.False(repo.Index.Conflicts.Any());
    }

    [Fact]
    public async Task SameLineChangedDifferently_StaysConflicted()
    {
        using var fixture = await ConflictAsync("one\ntwo\n", "ONE\ntwo\n", "uno\ntwo\n");
        var before = File.ReadAllText(Path.Combine(fixture.Path, "f.txt"));

        Assert.False(await _service.TryAutoResolveAsync(fixture.Path, "f.txt"));

        Assert.Equal(before, File.ReadAllText(Path.Combine(fixture.Path, "f.txt")));
        using var repo = new Repository(fixture.Path);
        Assert.True(repo.Index.Conflicts.Any());
    }
}
