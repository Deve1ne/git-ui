using GitUi.Domain.Entities;

namespace GitUi.Domain.ValueObjects;

public sealed record CommitGraphNode(GitCommit Commit, int Lane, IReadOnlyList<int> ParentLanes);
