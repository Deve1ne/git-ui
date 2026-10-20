using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using Xunit;

namespace GitUi.Application.Tests;

public class RepositoryChangeClassifierTests
{
    private const string WorkTree = @"C:\repo";
    private static readonly string[] GitDirectories = { @"C:\repo\.git" };

    [Theory]
    [InlineData(@"C:\repo\.git\HEAD")]
    [InlineData(@"C:\repo\.git\refs\heads\main")]
    [InlineData(@"C:\repo\.git\refs\remotes\origin\feature\x")]
    [InlineData(@"C:\repo\.git\refs\tags\v1")]
    [InlineData(@"C:\repo\.git\refs\stash")]
    [InlineData(@"C:\repo\.git\packed-refs")]
    [InlineData(@"C:\repo\.git\MERGE_HEAD")]
    [InlineData(@"C:\repo\.git\rebase-merge\done")]
    public void RefsHeadAndOperations_AreReferenceChanges(string path) =>
        Assert.Equal(RepositoryChangeKind.References, RepositoryChangeClassifier.Classify(path, WorkTree, GitDirectories));

    [Theory]
    [InlineData(@"C:\repo\.git\index")]
    [InlineData(@"C:\repo\src\Program.cs")]
    [InlineData(@"C:\repo\README.md")]
    public void IndexAndWorkTreeFiles_AreWorkingTreeChanges(string path) =>
        Assert.Equal(RepositoryChangeKind.WorkingTree, RepositoryChangeClassifier.Classify(path, WorkTree, GitDirectories));

    [Theory]
    [InlineData(@"C:\repo\.git\objects\ab\cdef")]
    [InlineData(@"C:\repo\.git\logs\HEAD")]
    [InlineData(@"C:\repo\.git\index.lock")]
    [InlineData(@"C:\repo\.git\refs\heads\main.lock")]
    [InlineData(@"C:\repo\.git\FETCH_HEAD")]
    [InlineData(@"C:\repo\.git\lfs\tmp\x")]
    [InlineData(@"C:\repo\.git")]
    [InlineData(@"C:\other\file.txt")]
    [InlineData(@"C:\repository\file.txt")]
    public void GitBookkeepingAndOutsidePaths_AreIgnored(string path) =>
        Assert.Null(RepositoryChangeClassifier.Classify(path, WorkTree, GitDirectories));

    [Fact]
    public void LinkedWorktree_RefsInTheCommonDirectoryCount()
    {
        var gitDirectories = new[] { @"C:\main\.git\worktrees\wt", @"C:\main\.git" };

        Assert.Equal(RepositoryChangeKind.References, RepositoryChangeClassifier.Classify(@"C:\main\.git\refs\heads\x", @"C:\wt", gitDirectories));
        Assert.Equal(RepositoryChangeKind.References, RepositoryChangeClassifier.Classify(@"C:\main\.git\worktrees\wt\HEAD", @"C:\wt", gitDirectories));
        Assert.Equal(RepositoryChangeKind.WorkingTree, RepositoryChangeClassifier.Classify(@"C:\wt\a.txt", @"C:\wt", gitDirectories));
    }
}
