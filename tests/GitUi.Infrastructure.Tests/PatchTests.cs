using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Processes;
using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class PatchTests
{
    private readonly CliGitRepositoryWriter _writer = new(new GitCliRunner(new ProcessRunner(), NullLogger<GitCliRunner>.Instance));

    [Fact]
    public async Task RestoreFileFromCommit_PutsThatVersionInTheWorkingDirectoryOnly()
    {
        using var fixture = new TestRepositoryFixture();
        var first = fixture.CommitFile("a.txt", "one\n", "first");
        fixture.CommitFile("a.txt", "two\n", "second");

        await _writer.RestoreFileFromCommitAsync(fixture.Path, first, "a.txt");

        Assert.Equal("one\n", File.ReadAllText(Path.Combine(fixture.Path, "a.txt")).Replace("\r\n", "\n"));
        using var repo = new Repository(fixture.Path);
        Assert.Equal(FileStatus.ModifiedInWorkdir, repo.RetrieveStatus("a.txt"));
    }

    [Fact]
    public async Task CreatePatch_ThenApplyOnTheParent_ReproducesTheChangeUncommitted()
    {
        using var fixture = new TestRepositoryFixture();
        var parent = fixture.CommitFile("a.txt", "one\n", "first");
        var child = fixture.CommitFile("a.txt", "one\ntwo\n", "second");

        var patch = await _writer.CreatePatchAsync(fixture.Path, child);
        Assert.StartsWith("From " + child, patch);

        var patchFile = Path.Combine(Path.GetTempPath(), $"gitui-test-{Guid.NewGuid():N}.patch");
        await File.WriteAllTextAsync(patchFile, patch);
        try
        {
            await _writer.ResetAsync(fixture.Path, parent, Application.Abstractions.GitResetMode.Hard);
            await _writer.ApplyPatchFileAsync(fixture.Path, patchFile);

            Assert.Equal("one\ntwo\n", File.ReadAllText(Path.Combine(fixture.Path, "a.txt")).Replace("\r\n", "\n"));
            using var repo = new Repository(fixture.Path);
            Assert.Equal(parent, repo.Head.Tip.Sha);
        }
        finally
        {
            File.Delete(patchFile);
        }
    }
}
