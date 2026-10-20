using GitUi.Application.DTOs;
using GitUi.Application.Services;
using GitUi.Domain.Entities;
using GitUi.Domain.ValueObjects;
using Xunit;

namespace GitUi.Application.Tests;

public class CommitSearchTests
{
    private static CommitGraphRow Row(string sha, string message, string author, params RefLabel[] labels) =>
        new(new CommitGraphNode(new GitCommit(sha, message, author, DateTimeOffset.UnixEpoch, Array.Empty<string>()), 0, new List<int> { 0 }), labels);

    private static readonly CommitGraphRow[] Rows =
    {
        Row("abc1234", "Fix login bug", "Alice", new RefLabel("feature/login", RefLabelKind.LocalBranch, false)),
        Row("def5678", "Add tests", "Bob"),
        Row("0123abc", "Release", "Carol", new RefLabel("v1.0", RefLabelKind.Tag, false)),
    };

    [Theory]
    [InlineData("LOGIN", new[] { 0 })]
    [InlineData("bob", new[] { 1 })]
    [InlineData("def5", new[] { 1 })]
    [InlineData("v1.", new[] { 2 })]
    [InlineData("abc", new[] { 0 })] // SHA prefix only: "0123abc" does not start with it
    [InlineData("  ", new int[0])]
    public void FindAll_MatchesMessageAuthorShaPrefixAndRefNames(string query, int[] expected)
    {
        Assert.Equal(expected, CommitSearch.FindAll(Rows, query));
    }
}
