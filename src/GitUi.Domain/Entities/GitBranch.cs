namespace GitUi.Domain.Entities;

public sealed record GitBranch(
    string Name,
    bool IsLocal,
    bool IsRemote,
    string? TrackingBranch,
    string TipSha,
    bool IsHead,
    int AheadBy = 0,
    int BehindBy = 0);
