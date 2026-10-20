using GitUi.Application.Abstractions;
using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Processes;
using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitUi.Infrastructure.Tests;

/// <summary>Branch operations against real repositories: one fresh repository per test.</summary>
public sealed class RefOperationsIntegrationTests : IDisposable
{
    private readonly TestRepositoryFixture _fixture = new();
    private readonly CliGitRepositoryWriter _writer = new(new GitCliRunner(new ProcessRunner(), NullLogger<GitCliRunner>.Instance));
    private readonly LibGit2SharpRepositoryReader _reader = new();
    private readonly string _mainBranch;

    public RefOperationsIntegrationTests()
    {
        _fixture.CommitFile("base.txt", "base", "base");
        using var repo = new Repository(_fixture.Path);
        _mainBranch = repo.Head.FriendlyName;
    }

    public void Dispose() => _fixture.Dispose();

    private void Checkout(string branch)
    {
        using var repo = new Repository(_fixture.Path);
        Commands.Checkout(repo, branch);
    }

    private void CreateBranch(string branch)
    {
        using var repo = new Repository(_fixture.Path);
        repo.CreateBranch(branch);
    }

    private string Tip(string branch)
    {
        using var repo = new Repository(_fixture.Path);
        return repo.Branches[branch].Tip.Sha;
    }

    [Fact]
    public async Task GetAncestryAsync_ReportsBehindAheadAndDiverged()
    {
        CreateBranch("feature");
        Checkout("feature");
        _fixture.CommitFile("f.txt", "f", "feature work");

        Assert.Equal(GitAncestry.FirstIsAncestor, await _reader.GetAncestryAsync(_fixture.Path, _mainBranch, "feature"));
        Assert.Equal(GitAncestry.SecondIsAncestor, await _reader.GetAncestryAsync(_fixture.Path, "feature", _mainBranch));

        Checkout(_mainBranch);
        _fixture.CommitFile("m.txt", "m", "main work");
        Assert.Equal(GitAncestry.Diverged, await _reader.GetAncestryAsync(_fixture.Path, _mainBranch, "feature"));
        Assert.Equal(GitAncestry.Same, await _reader.GetAncestryAsync(_fixture.Path, _mainBranch, _mainBranch));
    }

    [Fact]
    public async Task MergeAsync_NoFastForward_CreatesMergeCommitWithTwoParents()
    {
        CreateBranch("feature");
        Checkout("feature");
        _fixture.CommitFile("f.txt", "f", "feature work");
        Checkout(_mainBranch);

        await _writer.MergeAsync(_fixture.Path, "feature", GitMergeMode.NoFastForward);

        using var repo = new Repository(_fixture.Path);
        Assert.Equal(2, repo.Head.Tip.Parents.Count());
    }

    [Fact]
    public async Task FastForwardBranchAsync_BranchNotCheckedOut_MovesItAndRefusesNonFastForward()
    {
        CreateBranch("old");
        var newTip = _fixture.CommitFile("next.txt", "n", "next");

        await _writer.FastForwardBranchAsync(_fixture.Path, "old", _mainBranch);
        Assert.Equal(newTip, Tip("old"));

        // "old" now has its own commit: moving it back to an ancestor is not a fast-forward.
        CreateBranch("older");
        Checkout("old");
        _fixture.CommitFile("own.txt", "o", "own work on old");
        Checkout(_mainBranch);
        await Assert.ThrowsAnyAsync<Exception>(() => _writer.FastForwardBranchAsync(_fixture.Path, "old", "older"));
    }

    [Fact]
    public async Task RebaseAsync_OtherBranch_ReplaysItOntoTarget()
    {
        CreateBranch("feature");
        Checkout("feature");
        _fixture.CommitFile("f.txt", "f", "feature work");
        Checkout(_mainBranch);
        var mainTip = _fixture.CommitFile("m.txt", "m", "main work");

        await _writer.RebaseAsync(_fixture.Path, _mainBranch, "feature");

        using var repo = new Repository(_fixture.Path);
        Assert.Equal(mainTip, repo.Branches["feature"].Tip.Parents.Single().Sha);
    }

    [Fact]
    public async Task RevertAndCherryPick_ApplyWithoutEditor()
    {
        var toRevert = _fixture.CommitFile("r.txt", "r", "to revert");
        await _writer.RevertAsync(_fixture.Path, toRevert);
        Assert.False(File.Exists(Path.Combine(_fixture.Path, "r.txt")));

        CreateBranch("source");
        Checkout("source");
        var picked = _fixture.CommitFile("p.txt", "p", "to pick");
        Checkout(_mainBranch);
        await _writer.CherryPickAsync(_fixture.Path, picked);
        Assert.True(File.Exists(Path.Combine(_fixture.Path, "p.txt")));
    }

    [Fact]
    public async Task RenameAndTagOperations_Work()
    {
        CreateBranch("before");
        await _writer.RenameBranchAsync(_fixture.Path, "before", "after");
        await _writer.CreateTagAsync(_fixture.Path, "v1", Tip(_mainBranch), message: "release");

        using (var repo = new Repository(_fixture.Path))
        {
            Assert.Null(repo.Branches["before"]);
            Assert.NotNull(repo.Branches["after"]);
            Assert.True(repo.Tags["v1"].IsAnnotated);
        }

        await _writer.DeleteTagAsync(_fixture.Path, "v1");
        using var reopened = new Repository(_fixture.Path);
        Assert.Null(reopened.Tags["v1"]);
    }

    [Fact]
    public async Task PredictMergeAsync_ConflictingBranches_ListsFilesWithoutTouchingTheRepository()
    {
        CreateBranch("feature");
        Checkout("feature");
        _fixture.CommitFile("base.txt", "feature side", "feature edit");
        _fixture.CommitFile("other.txt", "only on feature", "feature add");
        Checkout(_mainBranch);
        var mainTip = _fixture.CommitFile("base.txt", "main side", "main edit");

        var prediction = await _reader.PredictMergeAsync(_fixture.Path, _mainBranch, "feature");

        Assert.True(prediction.HasConflicts);
        Assert.Equal(new[] { "base.txt" }, prediction.ConflictedFiles);
        Assert.Equal(GitOperationState.None, await _reader.GetOperationStateAsync(_fixture.Path));
        Assert.Equal(mainTip, Tip(_mainBranch));
        Assert.Empty(await _reader.GetStatusAsync(_fixture.Path));
    }

    [Fact]
    public async Task PredictMergeAsync_IndependentChanges_IsClean()
    {
        CreateBranch("feature");
        Checkout("feature");
        _fixture.CommitFile("f.txt", "f", "feature work");
        Checkout(_mainBranch);
        _fixture.CommitFile("m.txt", "m", "main work");

        Assert.False((await _reader.PredictMergeAsync(_fixture.Path, _mainBranch, "feature")).HasConflicts);
    }

    [Fact]
    public async Task ConflictingMerge_IsReportedAsInProgressAndCanBeAborted()
    {
        CreateBranch("feature");
        Checkout("feature");
        _fixture.CommitFile("base.txt", "feature side", "feature edit");
        Checkout(_mainBranch);
        var mainTip = _fixture.CommitFile("base.txt", "main side", "main edit");

        // A conflicting merge must surface as a merge conflict (handled as "resolve now"), not as a generic failure.
        await Assert.ThrowsAsync<GitUi.Domain.Exceptions.GitMergeConflictException>(() => _writer.MergeAsync(_fixture.Path, "feature", GitMergeMode.NoFastForward));
        Assert.Equal(GitOperationState.Merge, await _reader.GetOperationStateAsync(_fixture.Path));
        var conflicted = Assert.Single(await _reader.GetStatusAsync(_fixture.Path), c => c.Path == "base.txt");
        Assert.Equal(GitUi.Domain.Entities.FileChangeType.Conflicted, conflicted.ChangeType);
        Assert.False(conflicted.IsStaged);

        await _writer.AbortOperationAsync(_fixture.Path, GitOperationState.Merge);

        Assert.Equal(GitOperationState.None, await _reader.GetOperationStateAsync(_fixture.Path));
        Assert.Equal(mainTip, Tip(_mainBranch));
    }
}
