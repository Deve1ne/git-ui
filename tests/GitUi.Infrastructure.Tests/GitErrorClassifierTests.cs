using GitUi.Domain.Exceptions;
using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Processes;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class GitErrorClassifierTests
{
    [Theory]
    [InlineData("Authentication failed for 'https://example.com'", typeof(GitAuthenticationException))]
    [InlineData("CONFLICT (content): Merge conflict in file.txt", typeof(GitMergeConflictException))]
    [InlineData("fatal: Could not resolve host: example.com", typeof(GitNetworkException))]
    [InlineData("fatal: not a git repository (or any of the parent directories): .git", typeof(GitRepositoryCorruptException))]
    [InlineData("fatal: something completely unexpected", typeof(GitOperationException))]
    public void Classify_MapsStderrToExpectedExceptionType(string stderr, Type expectedType)
    {
        var result = new ProcessResult(1, string.Empty, stderr);

        var exception = GitErrorClassifier.Classify("git push", result);

        Assert.IsType(expectedType, exception);
        Assert.Equal(stderr, exception.Details);
    }

    [Fact]
    public void Classify_MergeConflictReportedOnStdout_IsMergeConflict()
    {
        // Exactly what `git merge --no-ff --no-edit <branch>` prints on a conflict: stdout only, empty stderr.
        var stdout = "Auto-merging c.txt\nCONFLICT (content): Merge conflict in c.txt\nAutomatic merge failed; fix conflicts and then commit the result.\n";
        var result = new ProcessResult(1, stdout, string.Empty);

        var exception = GitErrorClassifier.Classify("git merge --no-ff --no-edit origin/develop", result);

        var conflict = Assert.IsType<GitMergeConflictException>(exception);
        Assert.Contains("c.txt", conflict.Details);
    }

    [Fact]
    public void Classify_RebaseStoppedOnConflict_IsMergeConflict()
    {
        var result = new ProcessResult(1, "Auto-merging c.txt\nCONFLICT (content): Merge conflict in c.txt\n", "error: could not apply 5279e4f... f\nhint: Resolve all conflicts manually\n");

        Assert.IsType<GitMergeConflictException>(GitErrorClassifier.Classify("git rebase main", result));
    }
}
