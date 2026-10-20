using GitUi.Domain.Entities;
using GitUi.Domain.ValueObjects;

namespace GitUi.Domain.Services;

public sealed class CommitGraphLayoutBuilder
{
    public IReadOnlyList<CommitGraphNode> Build(IReadOnlyList<GitCommit> commits)
    {
        var lanes = new List<string?>();
        var nodes = new List<CommitGraphNode>(commits.Count);

        foreach (var commit in commits)
        {
            if (commit.IsStash && commit.ParentShas.Count > 0)
            {
                // A stash hangs off to the side of the commit it was made on: it reserves a lane
                // (never the first, so a stash on HEAD does not push the branch right) under a
                // marker its parent does not match, and the parent frees it when it arrives.
                var stashLane = ClaimLane(lanes, minimum: 1);
                lanes[stashLane] = StashMarker + commit.ParentShas[0];
                nodes.Add(new CommitGraphNode(commit, stashLane, new List<int> { stashLane }));
                continue;
            }

            for (var stashLane = 0; stashLane < lanes.Count; stashLane++)
            {
                if (lanes[stashLane] == StashMarker + commit.Sha)
                {
                    lanes[stashLane] = null;
                }
            }

            var lane = lanes.FindIndex(expectedSha => expectedSha == commit.Sha);
            if (lane == -1)
            {
                lane = ClaimLane(lanes);
            }

            // Any other lane also waiting for this same commit has now converged here
            // and will never be matched again — free it so it can be reused, instead of
            // leaking a lane for the rest of the history.
            for (var otherLane = 0; otherLane < lanes.Count; otherLane++)
            {
                if (otherLane != lane && lanes[otherLane] == commit.Sha)
                {
                    lanes[otherLane] = null;
                }
            }

            var parentLanes = new List<int> { lane };

            if (commit.ParentShas.Count == 0)
            {
                lanes[lane] = null;
            }
            else
            {
                lanes[lane] = commit.ParentShas[0];

                for (var i = 1; i < commit.ParentShas.Count; i++)
                {
                    var mergeParentSha = commit.ParentShas[i];
                    var existingLane = lanes.FindIndex(expectedSha => expectedSha == mergeParentSha);
                    if (existingLane == -1)
                    {
                        existingLane = ClaimLane(lanes);
                        lanes[existingLane] = mergeParentSha;
                    }

                    parentLanes.Add(existingLane);
                }
            }

            nodes.Add(new CommitGraphNode(commit, lane, parentLanes));
        }

        return nodes;
    }

    private const string StashMarker = "stash-of:";

    private static int ClaimLane(List<string?> lanes, int minimum = 0)
    {
        while (lanes.Count < minimum)
        {
            lanes.Add(null);
        }

        for (var lane = minimum; lane < lanes.Count; lane++)
        {
            if (lanes[lane] is null)
            {
                return lane;
            }
        }

        lanes.Add(null);
        return lanes.Count - 1;
    }
}
