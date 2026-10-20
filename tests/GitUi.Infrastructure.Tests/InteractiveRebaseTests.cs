using GitUi.Application.Services;
using GitUi.Domain.ValueObjects;
using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Processes;
using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class InteractiveRebaseTests
{
    private readonly InteractiveRebaseService _service = new(
        new LibGit2SharpRepositoryReader(),
        new CliGitRepositoryWriter(new GitCliRunner(new ProcessRunner(), NullLogger<GitCliRunner>.Instance)));

    [Fact]
    public async Task RunAsync_ReorderRewordSquashDrop_RewritesHistoryAsEdited()
    {
        using var fixture = new TestRepositoryFixture();
        var baseSha = fixture.CommitFile("base.txt", "base", "base");
        var one = fixture.CommitFile("one.txt", "1", "one");
        var two = fixture.CommitFile("two.txt", "2", "two");
        var three = fixture.CommitFile("three.txt", "3", "three");
        var four = fixture.CommitFile("four.txt", "4", "four");

        var commits = await _service.GetCommitsAsync(fixture.Path, baseSha);
        Assert.Equal(new[] { one, two, three, four }, commits.Select(c => c.Sha));

        await _service.RunAsync(fixture.Path, baseSha, new[]
        {
            new RebaseStep(three, RebaseAction.Reword, "three, reworded"),
            new RebaseStep(one, RebaseAction.Pick),
            new RebaseStep(two, RebaseAction.Fixup),
            new RebaseStep(four, RebaseAction.Drop),
        });

        using var repo = new Repository(fixture.Path);
        var log = repo.Commits.Take(3).Select(c => c.MessageShort).ToList();
        Assert.Equal(new[] { "one", "three, reworded", "base" }, log);
        Assert.False(File.Exists(Path.Combine(fixture.Path, "four.txt")));
        Assert.True(File.Exists(Path.Combine(fixture.Path, "two.txt")));
        Assert.Equal(CurrentOperation.None, repo.Info.CurrentOperation);
    }

    [Fact]
    public async Task RunAsync_WithUncommittedChanges_AutostashesThemAround()
    {
        using var fixture = new TestRepositoryFixture();
        var baseSha = fixture.CommitFile("base.txt", "base", "base");
        var one = fixture.CommitFile("one.txt", "1", "one");
        File.WriteAllText(Path.Combine(fixture.Path, "base.txt"), "work in progress");

        await _service.RunAsync(fixture.Path, baseSha, new[] { new RebaseStep(one, RebaseAction.Reword, "one, reworded") });

        using var repo = new Repository(fixture.Path);
        Assert.Equal("one, reworded", repo.Head.Tip.MessageShort);
        Assert.Equal("work in progress", File.ReadAllText(Path.Combine(fixture.Path, "base.txt")));
    }

    [Fact]
    public async Task PrepareRewordAsync_ReturnsParentAndCommits_RefusesRootAndForeignCommits()
    {
        using var fixture = new TestRepositoryFixture();
        var root = fixture.CommitFile("a.txt", "a", "root");
        var middle = fixture.CommitFile("b.txt", "b", "middle");
        fixture.CommitFile("c.txt", "c", "top");

        var (onto, commits) = await _service.PrepareRewordAsync(fixture.Path, middle);
        Assert.Equal(root, onto);
        Assert.Equal(2, commits.Count);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.PrepareRewordAsync(fixture.Path, root));

        using (var repo = new Repository(fixture.Path))
        {
            var side = repo.CreateBranch("side", root);
            Commands.Checkout(repo, side);
        }

        var foreign = fixture.CommitFile("d.txt", "d", "on side");
        using (var repo = new Repository(fixture.Path))
        {
            Commands.Checkout(repo, repo.Branches.First(b => b.FriendlyName is "main" or "master"));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.PrepareRewordAsync(fixture.Path, foreign));
    }

    [Fact]
    public async Task GetCommitsAsync_NothingOnTop_ExplainsWhy()
    {
        using var fixture = new TestRepositoryFixture();
        var head = fixture.CommitFile("a.txt", "a", "a");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GetCommitsAsync(fixture.Path, head));
    }
}
