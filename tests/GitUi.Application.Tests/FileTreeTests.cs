using GitUi.Application.Services;
using Xunit;

namespace GitUi.Application.Tests;

public class FileTreeTests
{
    private static string Render(IEnumerable<FileTreeRow<string>> rows) =>
        string.Join("|", rows.Select(r => new string('.', r.Depth) + (r.IsFolder ? r.Label + "/" : r.Label)));

    [Fact]
    public void Build_GroupsFilesUnderFoldersAndMergesSingleChildChains()
    {
        var paths = new[] { "README.md", "src/app/Main.cs", "src/app/View.cs", "src/lib/Util.cs", "docs/guide/intro.md" };

        var rows = FileTree.Build(paths, p => p, new HashSet<string>());

        Assert.Equal("docs/guide/|.intro.md|src/|.app/|..Main.cs|..View.cs|.lib/|..Util.cs|README.md", Render(rows));
        Assert.Equal("src/app", rows[3].FolderKey);
        Assert.Equal("src/app/Main.cs", rows[4].Item);
    }

    [Fact]
    public void Build_CollapsedFolder_HidesItsFiles()
    {
        var rows = FileTree.Build(new[] { "src/a.cs", "b.cs" }, p => p, new HashSet<string> { "src" });

        Assert.Equal("src/|b.cs", Render(rows));
        Assert.False(rows[0].IsExpanded);
    }
}
