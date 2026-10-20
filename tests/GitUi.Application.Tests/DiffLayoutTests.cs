using GitUi.Application.Services;
using GitUi.Domain.ValueObjects;
using Xunit;

namespace GitUi.Application.Tests;

public class DiffLayoutTests
{
    private static DiffLine C(string text) => new(DiffLineKind.Context, text);
    private static DiffLine R(string text) => new(DiffLineKind.Removed, text);
    private static DiffLine A(string text) => new(DiffLineKind.Added, text);

    [Fact]
    public void IgnoreWhitespace_IndentationOnlyChange_BecomesContextAndHunkDisappears()
    {
        var hunks = new[] { new DiffHunk(1, 1, new[] { C("a"), R("  b"), A("\tb"), C("c") }) };

        Assert.Empty(DiffLayout.IgnoreWhitespace(hunks));
    }

    [Fact]
    public void IgnoreWhitespace_KeepsRealChangesNextToWhitespaceOnes()
    {
        var hunks = new[]
        {
            new DiffHunk(1, 1, new[] { R("x = 1"), A("x  =  1"), C("y"), R("z"), A("Z") }),
        };

        var lines = Assert.Single(DiffLayout.IgnoreWhitespace(hunks)).Lines;

        Assert.Equal(new[] { DiffLineKind.Context, DiffLineKind.Context, DiffLineKind.Removed, DiffLineKind.Added }, lines.Select(l => l.Kind));
        Assert.Equal("x  =  1", lines[0].Content);
    }

    [Theory]
    [InlineData("var count = 1;", "var count = 42;", 12, 1, 2)]
    [InlineData("abc", "abXc", 2, 0, 1)]
    [InlineData("same", "same", 4, 0, 0)]
    public void ChangedSpan_IsWhatLiesBetweenCommonPrefixAndSuffix(string before, string after, int start, int oldLength, int newLength)
    {
        Assert.Equal((start, oldLength, newLength), DiffLayout.ChangedSpan(before, after));
    }

    [Fact]
    public void ChangedSpan_UnrelatedLines_HasNoHighlight()
    {
        Assert.Null(DiffLayout.ChangedSpan("alpha", "omega!"));
    }

    [Fact]
    public void SideBySide_PairsRemovedWithAddedAndPadsTheShorterSide()
    {
        var hunks = new[] { new DiffHunk(10, 20, new[] { C("ctx"), R("old1"), R("old2"), A("new1"), C("end") }) };

        var rows = DiffLayout.SideBySide(hunks);

        Assert.True(rows[0].IsHunkHeader);
        Assert.Equal((10, 20), (rows[1].Left!.LineNumber, rows[1].Right!.LineNumber));
        Assert.Equal(("old1", "new1"), (rows[2].Left!.Text, rows[2].Right!.Text));
        Assert.Equal((11, 21), (rows[2].Left!.LineNumber, rows[2].Right!.LineNumber));
        Assert.Equal("old2", rows[3].Left!.Text);
        Assert.Null(rows[3].Right);
        Assert.Equal((13, 22), (rows[4].Left!.LineNumber, rows[4].Right!.LineNumber));
    }
}
