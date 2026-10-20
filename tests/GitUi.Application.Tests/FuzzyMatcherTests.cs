using GitUi.Application.Services;
using Xunit;

namespace GitUi.Application.Tests;

public class FuzzyMatcherTests
{
    [Fact]
    public void Score_EveryWordMustAppear()
    {
        Assert.NotNull(FuzzyMatcher.Score("Checkout feature/login", "login check"));
        Assert.Null(FuzzyMatcher.Score("Checkout feature/login", "login push"));
    }

    [Fact]
    public void Filter_RanksWordStartsFirstAndKeepsOrderOtherwise()
    {
        var labels = new[] { "Stash changes", "Pop stash", "Fetch", "Unstash all" };

        var result = FuzzyMatcher.Filter(labels, l => l, "stash");

        Assert.Equal(new[] { "Stash changes", "Pop stash", "Unstash all" }, result);
    }

    [Fact]
    public void Filter_EmptyQuery_KeepsEverythingInOrder()
    {
        var labels = new[] { "b", "a" };

        Assert.Equal(labels, FuzzyMatcher.Filter(labels, l => l, "  "));
    }
}
