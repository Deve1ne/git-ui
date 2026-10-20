using GitUi.Application.Services;
using GitUi.Domain.Entities;
using Xunit;

namespace GitUi.Application.Tests;

public class RefTreeTests
{
    private static GitBranch Local(string name) => new(name, true, false, null, "sha", false);

    private static string Render(IEnumerable<RefTreeRow> rows) =>
        string.Join("|", rows.Select(r => new string('.', r.Depth) + (r.IsFolder ? r.Label + "/" : r.Label)));

    [Fact]
    public void Build_GroupsSlashedNamesIntoFoldersListedBeforeBranches()
    {
        var rows = RefTree.Build(new[] { Local("main"), Local("feature/b"), Local("feature/a"), Local("fix/deep/one"), Local("dev") }, "local", new HashSet<string>());

        Assert.Equal("feature/|.a|.b|fix/|.deep/|..one|dev|main", Render(rows));
    }

    [Fact]
    public void Build_CollapsedFolder_HidesItsContent()
    {
        var rows = RefTree.Build(new[] { Local("feature/a"), Local("main") }, "local", new HashSet<string> { "local/feature" });

        Assert.Equal("feature/|main", Render(rows));
        Assert.False(rows[0].IsExpanded);
    }

    [Fact]
    public void Build_ExpandAll_IgnoresCollapsedFolders()
    {
        var rows = RefTree.Build(new[] { Local("feature/a") }, "local", new HashSet<string> { "local/feature" }, expandAll: true);

        Assert.Equal("feature/|.a", Render(rows));
    }

    [Fact]
    public void Build_RootFolders_ShowEvenWithoutBranches()
    {
        var rows = RefTree.Build(Array.Empty<GitBranch>(), "remote", new HashSet<string>(), rootFolders: new[] { "upstream" });

        Assert.Equal("upstream/", Render(rows));
    }

    [Fact]
    public void Build_RemoteBranches_AreGroupedUnderTheirRemote()
    {
        var remote = new GitBranch("origin/feature/x", false, true, null, "sha", false);
        var rows = RefTree.Build(new[] { remote, new GitBranch("origin/main", false, true, null, "sha", false) }, "remote", new HashSet<string>());

        Assert.Equal("origin/|.feature/|..x|.main", Render(rows));
        Assert.Equal("remote/origin/feature", rows[1].FolderKey);
    }
}
