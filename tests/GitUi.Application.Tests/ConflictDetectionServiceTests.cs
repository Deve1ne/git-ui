using GitUi.Application.Services;
using GitUi.Domain.Entities;
using Xunit;

namespace GitUi.Application.Tests;

public class ConflictDetectionServiceTests
{
    private static GitBranch Local(string name, string sha = "l") => new(name, true, false, null, sha, false);

    private static GitBranch Remote(string name, string sha = "r") => new(name, false, true, null, sha, false);

    [Fact]
    public void ChooseTarget_ConfiguredTargetThatExists_Wins()
    {
        var branches = new[] { Local("feature"), Remote("origin/main"), Local("release") };

        Assert.Equal("release", ConflictDetectionService.ChooseTarget("feature", branches, "release"));
    }

    [Fact]
    public void ChooseTarget_FollowsOriginHead()
    {
        var branches = new[] { Local("feature"), Remote("origin/HEAD", "d"), Remote("origin/develop", "d"), Remote("origin/main", "m") };

        Assert.Equal("origin/develop", ConflictDetectionService.ChooseTarget("feature", branches, null));
    }

    [Fact]
    public void ChooseTarget_WithoutOriginHead_PrefersRemoteMainThenLocal()
    {
        Assert.Equal("origin/master", ConflictDetectionService.ChooseTarget("feature", new[] { Local("feature"), Local("main"), Remote("origin/master") }, null));
        Assert.Equal("main", ConflictDetectionService.ChooseTarget("feature", new[] { Local("feature"), Local("main") }, null));
    }

    [Fact]
    public void ChooseTarget_NeverTheCheckedOutBranch_AndIgnoresMissingConfiguredTarget()
    {
        var branches = new[] { Local("main") };

        Assert.Null(ConflictDetectionService.ChooseTarget("main", branches, "gone"));
    }
}
