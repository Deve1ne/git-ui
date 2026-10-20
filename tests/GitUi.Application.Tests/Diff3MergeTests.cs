using GitUi.Application.Services;
using Xunit;

namespace GitUi.Application.Tests;

public class Diff3MergeTests
{
    private static string[] L(string text) => text.Split('|');

    [Fact]
    public void ChangesInDifferentPlaces_AreBothKept()
    {
        var merged = Diff3Merge.TryMerge(L("a|b|c|d|e"), L("A|b|c|d|e"), L("a|b|c|d|E"));

        Assert.Equal(L("A|b|c|d|E"), merged);
    }

    [Fact]
    public void IdenticalChangeOnBothSides_IsNotAConflict()
    {
        var merged = Diff3Merge.TryMerge(L("a|b|c"), L("a|X|c"), L("a|X|c"));

        Assert.Equal(L("a|X|c"), merged);
    }

    [Fact]
    public void DifferentChangesToTheSameLine_AreATrueConflict()
    {
        Assert.Null(Diff3Merge.TryMerge(L("a|b|c"), L("a|X|c"), L("a|Y|c")));
    }

    [Fact]
    public void InsertionsAndDeletions_OnEachSide_Merge()
    {
        var merged = Diff3Merge.TryMerge(
            L("1|2|3|4|5|6|7|8"),
            L("1|2|new|3|4|5|6|7|8"),     // ours inserts after 2
            L("1|2|3|4|5|6|8"));          // theirs deletes 7

        Assert.Equal(L("1|2|new|3|4|5|6|8"), merged);
    }

    [Fact]
    public void OneSideUnchanged_TakesTheOtherSide()
    {
        var merged = Diff3Merge.TryMerge(L("a|b"), L("a|b"), L("x|y|z"));

        Assert.Equal(L("x|y|z"), merged);
    }

    [Fact]
    public void Hunks_ReportTheChangedRanges()
    {
        var hunks = Diff3Merge.Hunks(L("a|b|c|d"), L("a|x|c|d|e"));

        Assert.Equal(new[] { new Diff3Merge.Hunk(1, 1, 1, 1), new Diff3Merge.Hunk(4, 0, 4, 1) }, hunks);
    }
}
