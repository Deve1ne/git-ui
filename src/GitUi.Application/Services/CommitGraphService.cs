using GitUi.Application.Abstractions;
using GitUi.Application.DTOs;
using GitUi.Domain.Services;

namespace GitUi.Application.Services;

public sealed class CommitGraphService
{
    private readonly IGitRepositoryReader _reader;
    private readonly CommitGraphLayoutBuilder _layoutBuilder;

    public CommitGraphService(IGitRepositoryReader reader, CommitGraphLayoutBuilder layoutBuilder)
    {
        _reader = reader;
        _layoutBuilder = layoutBuilder;
    }

    public Task<IReadOnlyList<CommitGraphRow>> GetGraphAsync(string path, CancellationToken cancellationToken = default)
        => GetGraphAsync(path, GraphRefFilter.Empty, cancellationToken);

    public async Task<IReadOnlyList<CommitGraphRow>> GetGraphAsync(string path, GraphRefFilter filter, CancellationToken cancellationToken = default)
    {
        var commits = filter.IsEmpty
            ? await _reader.GetCommitsAsync(path, cancellationToken)
            : await _reader.GetCommitsAsync(path, filter, cancellationToken);
        var nodes = _layoutBuilder.Build(commits);

        var branches = await _reader.GetBranchesAsync(path, cancellationToken);
        var tags = await _reader.GetTagsAsync(path, cancellationToken);

        var labelsBySha = new Dictionary<string, List<RefLabel>>();

        foreach (var branch in branches)
        {
            // A hidden branch loses its label too, even where its tip is still drawn for another branch.
            if (filter.Hidden.Contains(GraphRefFilter.RefName(branch)))
            {
                continue;
            }

            var kind = branch.IsRemote ? RefLabelKind.RemoteBranch : RefLabelKind.LocalBranch;
            AddLabel(labelsBySha, branch.TipSha, new RefLabel(branch.Name, kind, branch.IsHead));
        }

        foreach (var tag in tags)
        {
            AddLabel(labelsBySha, tag.TargetSha, new RefLabel(tag.Name, RefLabelKind.Tag, IsHead: false));
        }

        return nodes
            .Select(node => new CommitGraphRow(
                node,
                labelsBySha.TryGetValue(node.Commit.Sha, out var labels) ? labels : Array.Empty<RefLabel>()))
            .ToList();
    }

    private static void AddLabel(Dictionary<string, List<RefLabel>> labelsBySha, string sha, RefLabel label)
    {
        if (!labelsBySha.TryGetValue(sha, out var list))
        {
            list = new List<RefLabel>();
            labelsBySha[sha] = list;
        }

        list.Add(label);
    }
}
