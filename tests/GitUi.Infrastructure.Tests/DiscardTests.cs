using GitUi.Application.Services;
using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Processes;
using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class DiscardTests
{
    private readonly CliGitRepositoryWriter _writer = new(new GitCliRunner(new ProcessRunner(), NullLogger<GitCliRunner>.Instance));

    [Fact]
    public async Task DiscardWorkingChangesAsync_TrackedAndUntracked_RestoresAndDeletes()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("a.txt", "committed", "seed");
        File.WriteAllText(Path.Combine(fixture.Path, "a.txt"), "edited");
        File.WriteAllText(Path.Combine(fixture.Path, "new.txt"), "untracked");

        await _writer.DiscardWorkingChangesAsync(fixture.Path, new[] { "a.txt" }, new[] { "new.txt" });

        Assert.Equal("committed", File.ReadAllText(Path.Combine(fixture.Path, "a.txt")));
        Assert.False(File.Exists(Path.Combine(fixture.Path, "new.txt")));
    }

    [Fact]
    public async Task DiscardWorkingChangesAsync_StagedThenEdited_KeepsStagedContent()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("a.txt", "committed", "seed");
        File.WriteAllText(Path.Combine(fixture.Path, "a.txt"), "staged");
        await _writer.StageAsync(fixture.Path, "a.txt");
        File.WriteAllText(Path.Combine(fixture.Path, "a.txt"), "staged then edited");

        await _writer.DiscardWorkingChangesAsync(fixture.Path, new[] { "a.txt" }, Array.Empty<string>());

        Assert.Equal("staged", File.ReadAllText(Path.Combine(fixture.Path, "a.txt")));
    }

    [Fact]
    public async Task ApplyPatchToWorkingTreeAsync_ReverseOneLine_DiscardsOnlyThatLine()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("lines.txt", "one\ntwo\nthree\n", "seed");
        File.WriteAllText(Path.Combine(fixture.Path, "lines.txt"), "ONE\ntwo\nTHREE\n");
        var hunk = Assert.Single(await new LibGit2SharpRepositoryReader().GetDiffAsync(fixture.Path, "lines.txt", staged: false));
        var firstChange = hunk.Lines.Select((line, index) => (line, index))
            .Where(x => x.line.Content is "one" or "ONE")
            .Select(x => x.index)
            .ToHashSet();

        // Discarding is the reverse of the working-tree change: the worktree holds the new side.
        var patch = DiffPatchBuilder.BuildPatch("lines.txt", hunk, firstChange, forUnstage: true)!;
        await _writer.ApplyPatchToWorkingTreeAsync(fixture.Path, patch, reverse: true);

        // git apply writes the worktree with the machine's core.autocrlf, so compare line contents.
        Assert.Equal("one\ntwo\nTHREE\n", File.ReadAllText(Path.Combine(fixture.Path, "lines.txt")).Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task ApplyPatchToWorkingTreeAsync_LastLineWithoutNewline_DiscardsIt()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("lines.txt", "base\n2\n3\n", "seed");
        File.WriteAllText(Path.Combine(fixture.Path, "lines.txt"), "BASE\n2\nthree\nfour");
        var hunk = Assert.Single(await new LibGit2SharpRepositoryReader().GetDiffAsync(fixture.Path, "lines.txt", staged: false));
        var four = hunk.Lines.Select((line, index) => (line, index)).Where(x => x.line.Content == "four").Select(x => x.index).ToHashSet();
        Assert.True(hunk.Lines.Single(l => l.Content == "four").NoNewlineAtEnd);

        var patch = DiffPatchBuilder.BuildPatch("lines.txt", hunk, four, forUnstage: true)!;
        await _writer.ApplyPatchToWorkingTreeAsync(fixture.Path, patch, reverse: true);

        Assert.Equal("BASE\n2\nthree\n", File.ReadAllText(Path.Combine(fixture.Path, "lines.txt")).Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task DiscardAllAsync_ThenRestore_BringsBackStagedUnstagedAndUntracked()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("a.txt", "committed", "seed");
        fixture.CommitFile("b.txt", "committed", "seed b");
        File.WriteAllText(Path.Combine(fixture.Path, "a.txt"), "staged");
        await _writer.StageAsync(fixture.Path, "a.txt");
        File.WriteAllText(Path.Combine(fixture.Path, "b.txt"), "unstaged");
        File.WriteAllText(Path.Combine(fixture.Path, "new.txt"), "untracked");

        var sha = await _writer.DiscardAllAsync(fixture.Path);

        using (var repo = new Repository(fixture.Path))
        {
            Assert.NotNull(sha);
            Assert.False(repo.RetrieveStatus().IsDirty);
            Assert.Empty(repo.Stashes);
        }

        await _writer.RestoreDiscardedAsync(fixture.Path, sha!);

        using (var repo = new Repository(fixture.Path))
        {
            Assert.Equal(FileStatus.ModifiedInIndex, repo.RetrieveStatus("a.txt"));
            Assert.Equal(FileStatus.ModifiedInWorkdir, repo.RetrieveStatus("b.txt"));
            Assert.Equal(FileStatus.NewInWorkdir, repo.RetrieveStatus("new.txt"));
        }
    }

    [Fact]
    public async Task DiscardAllAsync_CleanRepository_ReturnsNullAndKeepsExistingStash()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("a.txt", "committed", "seed");
        File.WriteAllText(Path.Combine(fixture.Path, "a.txt"), "mine");
        await _writer.StashSaveAsync(fixture.Path, "user stash");

        var sha = await _writer.DiscardAllAsync(fixture.Path);

        using var repo = new Repository(fixture.Path);
        Assert.Null(sha);
        Assert.Single(repo.Stashes);
    }
}
