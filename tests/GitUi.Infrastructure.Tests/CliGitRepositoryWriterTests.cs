using GitUi.Application.Abstractions;
using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Processes;
using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class CliGitRepositoryWriterTests : IClassFixture<TestRepositoryFixture>
{
    private readonly TestRepositoryFixture _fixture;
    private readonly CliGitRepositoryWriter _writer;

    public CliGitRepositoryWriterTests(TestRepositoryFixture fixture)
    {
        _fixture = fixture;
        var runner = new GitCliRunner(new ProcessRunner(), NullLogger<GitCliRunner>.Instance);
        _writer = new CliGitRepositoryWriter(runner);
    }

    [Fact]
    public async Task StageAndCommitAsync_NewFile_CreatesCommit()
    {
        var filePath = Path.Combine(_fixture.Path, "cli-file.txt");
        File.WriteAllText(filePath, "content");

        await _writer.StageAsync(_fixture.Path, "cli-file.txt");
        await _writer.CommitAsync(_fixture.Path, "commit via cli writer");

        using var repo = new Repository(_fixture.Path);
        Assert.Equal("commit via cli writer", repo.Head.Tip.MessageShort);
    }

    [Fact]
    public async Task CreateBranchAsync_NewBranchName_BranchExistsAfterwards()
    {
        _fixture.CommitFile("seed.txt", "seed", "seed commit");

        await _writer.CreateBranchAsync(_fixture.Path, "feature/test-branch");

        using var repo = new Repository(_fixture.Path);
        Assert.Contains(repo.Branches, b => b.FriendlyName == "feature/test-branch");
    }

    // The tests below mutate HEAD, the index or the stash list, so each one owns an
    // isolated repository instead of the shared class fixture.

    [Fact]
    public async Task CheckoutAsync_ExistingBranch_MovesHeadToThatBranch()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("seed.txt", "seed", "seed commit");
        await _writer.CreateBranchAsync(fixture.Path, "feature/switch-target");

        await _writer.CheckoutAsync(fixture.Path, "feature/switch-target");

        using var repo = new Repository(fixture.Path);
        Assert.Equal("feature/switch-target", repo.Head.FriendlyName);
    }

    [Fact]
    public async Task UnstageAsync_StagedFile_LeavesFileUnstaged()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("seed.txt", "seed", "seed commit");
        File.WriteAllText(Path.Combine(fixture.Path, "unstage-me.txt"), "content");
        await _writer.StageAsync(fixture.Path, "unstage-me.txt");

        await _writer.UnstageAsync(fixture.Path, "unstage-me.txt");

        using var repo = new Repository(fixture.Path);
        var entry = Assert.Single(repo.RetrieveStatus(), e => e.FilePath == "unstage-me.txt");
        Assert.Equal(FileStatus.NewInWorkdir, entry.State);
    }

    [Fact]
    public async Task DeleteBranchAsync_ExistingBranch_RemovesBranch()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("seed.txt", "seed", "seed commit");
        await _writer.CreateBranchAsync(fixture.Path, "feature/doomed");

        await _writer.DeleteBranchAsync(fixture.Path, "feature/doomed", force: true);

        using var repo = new Repository(fixture.Path);
        Assert.DoesNotContain(repo.Branches, b => b.FriendlyName == "feature/doomed");
    }

    [Fact]
    public async Task StashSaveAsync_UncommittedChange_CleansWorkingTreeAndRecordsStash()
    {
        using var fixture = new TestRepositoryFixture();
        var filePath = Path.Combine(fixture.Path, "stash-me.txt");
        fixture.CommitFile("stash-me.txt", "committed", "seed commit");
        File.WriteAllText(filePath, "uncommitted");

        await _writer.StashSaveAsync(fixture.Path, "work in progress");

        using var repo = new Repository(fixture.Path);
        Assert.False(repo.RetrieveStatus().IsDirty);
        Assert.Equal("committed", File.ReadAllText(filePath));
        Assert.Single(repo.Stashes);
    }

    [Fact]
    public async Task StashApplyAsync_SavedStash_RestoresChangeAndKeepsStash()
    {
        using var fixture = new TestRepositoryFixture();
        var filePath = Path.Combine(fixture.Path, "stash-me.txt");
        fixture.CommitFile("stash-me.txt", "committed", "seed commit");
        File.WriteAllText(filePath, "uncommitted");
        await _writer.StashSaveAsync(fixture.Path, "work in progress");

        await _writer.StashApplyAsync(fixture.Path, 0);

        using var repo = new Repository(fixture.Path);
        Assert.Equal("uncommitted", File.ReadAllText(filePath));
        Assert.Single(repo.Stashes);
    }

    [Fact]
    public async Task StashPopAsync_SavedStash_RestoresChangeAndRemovesStash()
    {
        using var fixture = new TestRepositoryFixture();
        var filePath = Path.Combine(fixture.Path, "stash-me.txt");
        fixture.CommitFile("stash-me.txt", "committed", "seed commit");
        File.WriteAllText(filePath, "uncommitted");
        await _writer.StashSaveAsync(fixture.Path, "work in progress");

        await _writer.StashPopAsync(fixture.Path, 0);

        using var repo = new Repository(fixture.Path);
        Assert.Equal("uncommitted", File.ReadAllText(filePath));
        Assert.Empty(repo.Stashes);
    }

    [Fact]
    public async Task StashDropAsync_SavedStash_RemovesStashWithoutRestoringChange()
    {
        using var fixture = new TestRepositoryFixture();
        var filePath = Path.Combine(fixture.Path, "stash-me.txt");
        fixture.CommitFile("stash-me.txt", "committed", "seed commit");
        File.WriteAllText(filePath, "uncommitted");
        await _writer.StashSaveAsync(fixture.Path, "work in progress");

        await _writer.StashDropAsync(fixture.Path, 0);

        using var repo = new Repository(fixture.Path);
        Assert.Empty(repo.Stashes);
        Assert.Equal("committed", File.ReadAllText(filePath));
    }

    [Fact]
    public async Task ResetAsync_HardToPreviousCommit_MovesHeadBackAndRestoresContent()
    {
        using var fixture = new TestRepositoryFixture();
        var filePath = Path.Combine(fixture.Path, "reset-me.txt");
        var firstSha = fixture.CommitFile("reset-me.txt", "first", "first commit");
        fixture.CommitFile("reset-me.txt", "second", "second commit");

        await _writer.ResetAsync(fixture.Path, firstSha, GitResetMode.Hard);

        using var repo = new Repository(fixture.Path);
        Assert.Equal(firstSha, repo.Head.Tip.Sha);
        Assert.Equal("first", File.ReadAllText(filePath));
    }

    [Fact]
    public async Task ApplyPatchToIndexAsync_OneSelectedLine_StagesOnlyThatLine()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("lines.txt", "one\ntwo\nthree\n", "seed");
        File.WriteAllText(Path.Combine(fixture.Path, "lines.txt"), "ONE\ntwo\nTHREE\n");

        var hunks = await new LibGit2SharpRepositoryReader().GetDiffAsync(fixture.Path, "lines.txt", staged: false);
        var hunk = Assert.Single(hunks);
        var firstChange = hunk.Lines.Select((line, index) => (line, index))
            .Where(x => x.line.Content is "one" or "ONE")
            .Select(x => x.index)
            .ToHashSet();
        var patch = GitUi.Application.Services.DiffPatchBuilder.BuildPatch("lines.txt", hunk, firstChange, forUnstage: false)!;

        var writer = new CliGitRepositoryWriter(new GitCliRunner(new ProcessRunner(), NullLogger<GitCliRunner>.Instance));
        await writer.ApplyPatchToIndexAsync(fixture.Path, patch, reverse: false);

        using var repo = new Repository(fixture.Path);
        var staged = ((Blob)repo.Lookup(repo.Index["lines.txt"].Id)).GetContentText();
        Assert.Equal("ONE\ntwo\nthree\n", staged);
    }

    [Fact]
    public async Task CreateBranchAsync_WithStartPoint_BranchPointsAtThatCommit()
    {
        using var fixture = new TestRepositoryFixture();
        var firstSha = fixture.CommitFile("a.txt", "one", "first");
        fixture.CommitFile("a.txt", "two", "second");

        await _writer.CreateBranchAsync(fixture.Path, "from-first", firstSha);

        using var repo = new Repository(fixture.Path);
        Assert.Equal(firstSha, repo.Branches["from-first"].Tip.Sha);
    }

    [Fact]
    public async Task PullAsync_FastForwardOnly_MovesHeadToUpstream()
    {
        using var upstream = new TestRepositoryFixture();
        upstream.CommitFile("a.txt", "one", "first");
        var clonePath = Path.Combine(Path.GetTempPath(), "gitui-clone-" + Guid.NewGuid());
        Repository.Clone(upstream.Path, clonePath);
        var newSha = upstream.CommitFile("a.txt", "two", "second");

        try
        {
            await _writer.PullAsync(clonePath, GitPullMode.FastForwardOnly);

            using var repo = new Repository(clonePath);
            Assert.Equal(newSha, repo.Head.Tip.Sha);
        }
        finally
        {
            ForceDeleteDirectory(clonePath);
        }
    }

    [Fact]
    public async Task PullAsync_WithUncommittedChange_AutostashesIt()
    {
        using var upstream = new TestRepositoryFixture();
        upstream.CommitFile("a.txt", "one", "first");
        var clonePath = Path.Combine(Path.GetTempPath(), "gitui-clone-" + Guid.NewGuid());
        Repository.Clone(upstream.Path, clonePath);
        var newSha = upstream.CommitFile("b.txt", "two", "second");
        File.WriteAllText(Path.Combine(clonePath, "a.txt"), "local edit");

        try
        {
            await _writer.PullAsync(clonePath, GitPullMode.Rebase);

            using var repo = new Repository(clonePath);
            Assert.Equal(newSha, repo.Head.Tip.Sha);
            Assert.Equal("local edit", File.ReadAllText(Path.Combine(clonePath, "a.txt")));
        }
        finally
        {
            ForceDeleteDirectory(clonePath);
        }
    }

    private static void ForceDeleteDirectory(string path)
    {
        foreach (var file in new DirectoryInfo(path).GetFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        Directory.Delete(path, recursive: true);
    }
}
