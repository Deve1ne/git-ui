using GitUi.Application.Services;
using GitUi.Domain.ValueObjects;
using Xunit;

namespace GitUi.Application.Tests;

public class DiffPatchBuilderTests
{
    // old: a, b, c   new: a, B, c
    private static readonly DiffHunk ReplaceMiddleLine = new(1, 1, new[]
    {
        new DiffLine(DiffLineKind.Context, "a"),
        new DiffLine(DiffLineKind.Removed, "b"),
        new DiffLine(DiffLineKind.Added, "B"),
        new DiffLine(DiffLineKind.Context, "c"),
    });

    [Fact]
    public void BuildPatch_WholeHunk_KeepsEveryLineAndCountsBothSides()
    {
        var patch = DiffPatchBuilder.BuildPatch("dir/f.txt", ReplaceMiddleLine, selectedLineIndexes: null, forUnstage: false);

        Assert.Equal(
            "diff --git a/dir/f.txt b/dir/f.txt\n--- a/dir/f.txt\n+++ b/dir/f.txt\n@@ -1,3 +1,3 @@\n a\n-b\n+B\n c\n",
            patch);
    }

    [Fact]
    public void BuildPatch_StagingOnlyTheRemoval_DropsUnselectedAddition()
    {
        var patch = DiffPatchBuilder.BuildPatch("f.txt", ReplaceMiddleLine, new HashSet<int> { 1 }, forUnstage: false);

        Assert.EndsWith("@@ -1,3 +1,2 @@\n a\n-b\n c\n", patch);
    }

    [Fact]
    public void BuildPatch_StagingOnlyTheAddition_TurnsUnselectedRemovalIntoContext()
    {
        var patch = DiffPatchBuilder.BuildPatch("f.txt", ReplaceMiddleLine, new HashSet<int> { 2 }, forUnstage: false);

        Assert.EndsWith("@@ -1,3 +1,4 @@\n a\n b\n+B\n c\n", patch);
    }

    [Fact]
    public void BuildPatch_UnstagingOnlyTheRemoval_TurnsUnselectedAdditionIntoContext()
    {
        var patch = DiffPatchBuilder.BuildPatch("f.txt", ReplaceMiddleLine, new HashSet<int> { 1 }, forUnstage: true);

        Assert.EndsWith("@@ -1,4 +1,3 @@\n a\n-b\n B\n c\n", patch);
    }

    [Fact]
    public void BuildPatch_SelectionWithoutChanges_ReturnsNull()
    {
        Assert.Null(DiffPatchBuilder.BuildPatch("f.txt", ReplaceMiddleLine, new HashSet<int> { 0, 3 }, forUnstage: false));
    }
}
