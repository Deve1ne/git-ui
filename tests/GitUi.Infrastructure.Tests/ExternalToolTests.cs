using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Processes;
using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitUi.Infrastructure.Tests;

// The "tools" are shell one-liners configured in the test repository only, so they run headless
// and never touch the developer's global git config.
public class ExternalToolTests
{
    private readonly CliGitRepositoryWriter _writer = new(new GitCliRunner(new ProcessRunner(), NullLogger<GitCliRunner>.Instance));

    private static void Configure(string path, params (string Key, string Value)[] entries)
    {
        using var repo = new Repository(path);
        foreach (var (key, value) in entries)
        {
            repo.Config.Set(key, value, ConfigurationLevel.Local);
        }
    }

    [Fact]
    public async Task OpenDiffTool_RunsTheConfiguredToolOnTheFile()
    {
        using var fixture = new TestRepositoryFixture();
        var first = fixture.CommitFile("a.txt", "one\n", "first");
        var second = fixture.CommitFile("a.txt", "two\n", "second");
        File.WriteAllText(Path.Combine(fixture.Path, "a.txt"), "three\n");
        Configure(fixture.Path, ("diff.tool", "fake"), ("difftool.fake.cmd", "cat \"$REMOTE\" >> difftool-calls.txt"));

        await _writer.OpenDiffToolAsync(fixture.Path, "a.txt", staged: false, commitSha: null);
        await _writer.OpenDiffToolAsync(fixture.Path, "a.txt", staged: false, commitSha: second);
        await _writer.OpenDiffToolAsync(fixture.Path, "a.txt", staged: false, commitSha: second, compareFromSha: first);

        var calls = File.ReadAllText(Path.Combine(fixture.Path, "difftool-calls.txt")).Replace("\r\n", "\n");
        Assert.Equal("three\ntwo\ntwo\n", calls);
    }

    [Fact]
    public async Task OpenDiffTool_NoToolConfigured_ExplainsHowToSetOne()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("a.txt", "one\n", "first");
        // Override any global setting with an empty value for this repository.
        Configure(fixture.Path, ("diff.tool", string.Empty), ("merge.tool", string.Empty));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _writer.OpenDiffToolAsync(fixture.Path, "a.txt", false, null));
        Assert.Contains("diff.tool", error.Message);
    }

    [Fact]
    public async Task OpenMergeTool_ToolResolvesTheConflict()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("a.txt", "base\n", "base");
        using (var repo = new Repository(fixture.Path))
        {
            repo.CreateBranch("other");
        }

        fixture.CommitFile("a.txt", "mine\n", "mine");
        string mainBranch;
        using (var repo = new Repository(fixture.Path))
        {
            mainBranch = repo.Head.FriendlyName;
            Commands.Checkout(repo, "other");
        }

        fixture.CommitFile("a.txt", "theirs\n", "theirs");
        using (var repo = new Repository(fixture.Path))
        {
            Commands.Checkout(repo, mainBranch);
        }

        await Assert.ThrowsAnyAsync<Exception>(() => _writer.MergeAsync(fixture.Path, "other", Application.Abstractions.GitMergeMode.NoFastForward));
        Configure(fixture.Path,
            ("merge.tool", "fake"),
            ("mergetool.fake.cmd", "cp \"$REMOTE\" \"$MERGED\""),
            ("mergetool.fake.trustExitCode", "true"));

        await _writer.OpenMergeToolAsync(fixture.Path, "a.txt");

        Assert.Equal("theirs\n", File.ReadAllText(Path.Combine(fixture.Path, "a.txt")).Replace("\r\n", "\n"));
        using var after = new Repository(fixture.Path);
        Assert.False(after.Index.Conflicts.Any());
        Assert.False(File.Exists(Path.Combine(fixture.Path, "a.txt.orig")));
    }
}
