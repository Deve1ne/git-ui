using GitUi.Application.Services;
using Xunit;

namespace GitUi.Application.Tests;

public class ConflictDocumentTests
{
    private const string Conflicted =
        "top\n" +
        "<<<<<<< HEAD\n" +
        "ours one\n" +
        "ours two\n" +
        "=======\n" +
        "theirs one\n" +
        ">>>>>>> feature\n" +
        "bottom\n";

    [Fact]
    public void Parse_SplitsStableLinesAndConflict()
    {
        var document = ConflictDocument.Parse(Conflicted);

        Assert.Equal(3, document.Segments.Count);
        var conflict = Assert.Single(document.Conflicts);
        Assert.Equal(new[] { "ours one", "ours two" }, conflict.Ours);
        Assert.Equal(new[] { "theirs one" }, conflict.Theirs);
        Assert.Equal("HEAD", conflict.OursLabel);
        Assert.Equal("feature", conflict.TheirsLabel);
        Assert.Null(conflict.Base);
    }

    [Fact]
    public void Compose_TakingLinesFromBothSides_KeepsOursThenTheirs()
    {
        var document = ConflictDocument.Parse(Conflicted);

        var output = document.Compose(new[] { new ConflictResolution(new HashSet<int> { 1 }, new HashSet<int> { 0 }) });

        Assert.Equal("top\nours two\ntheirs one\nbottom\n", output);
    }

    [Fact]
    public void ComposeLines_TagsEveryLineWithItsOrigin()
    {
        var document = ConflictDocument.Parse(Conflicted);

        var lines = document.ComposeLines(new[] { new ConflictResolution(new HashSet<int> { 1 }, new HashSet<int> { 0 }) });

        Assert.Equal(
            new[]
            {
                ("top", MergeLineOrigin.Stable, -1),
                ("ours two", MergeLineOrigin.Ours, 0),
                ("theirs one", MergeLineOrigin.Theirs, 0),
                ("bottom", MergeLineOrigin.Stable, -1),
            },
            lines.Select(l => (l.Text, l.Origin, l.ConflictIndex)));
        Assert.Equal(new[] { 1, 2, 3, 4 }, lines.Select(l => l.LineNumber));
    }

    [Fact]
    public void ComposeLines_UnresolvedConflict_IsMarkedUnresolved()
    {
        var lines = ConflictDocument.Parse(Conflicted).ComposeLines(new ConflictResolution?[] { null });

        Assert.All(lines.Where(l => l.ConflictIndex == 0), l => Assert.Equal(MergeLineOrigin.Unresolved, l.Origin));
        Assert.Contains(lines, l => l.Text.StartsWith("<<<<<<<", StringComparison.Ordinal));
    }

    [Fact]
    public void Compose_WithoutResolution_KeepsTheMarkers()
    {
        var document = ConflictDocument.Parse(Conflicted);

        Assert.Equal(Conflicted, document.Compose(new ConflictResolution?[] { null }));
    }

    [Fact]
    public void Parse_Diff3Markers_ReadsBaseSeparately()
    {
        var text = "<<<<<<< ours\na\n||||||| base\nb\n=======\nc\n>>>>>>> theirs\n";

        var conflict = Assert.Single(ConflictDocument.Parse(text).Conflicts);

        Assert.Equal(new[] { "a" }, conflict.Ours);
        Assert.Equal(new[] { "b" }, conflict.Base!);
        Assert.Equal(new[] { "c" }, conflict.Theirs);
    }

    [Fact]
    public void ParseAndCompose_CrLfFile_KeepsCrLf()
    {
        var text = Conflicted.Replace("\n", "\r\n");

        var output = ConflictDocument.Parse(text).Compose(new[] { ConflictResolution.AllTheirs(ConflictDocument.Parse(text).Conflicts[0]) });

        Assert.Equal("top\r\ntheirs one\r\nbottom\r\n", output);
    }

    [Fact]
    public void Parse_UnterminatedMarker_IsPlainText()
    {
        var document = ConflictDocument.Parse("<<<<<<< not a conflict\nline\n");

        Assert.Empty(document.Conflicts);
    }

    [Theory]
    [InlineData("https://github.com/org/repo.git", "repo")]
    [InlineData("git@github.com:org/repo.git", "repo")]
    [InlineData("https://dev.azure.com/org/project/_git/My.Repo", "My.Repo")]
    [InlineData("C:\\src\\local-repo\\", "local-repo")]
    public void DeriveFolderName_UsesLastSegmentWithoutGitSuffix(string url, string expected)
    {
        Assert.Equal(expected, RepositorySetupService.DeriveFolderName(url));
    }
}
