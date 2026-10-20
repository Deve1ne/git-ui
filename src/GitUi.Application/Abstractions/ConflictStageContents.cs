namespace GitUi.Application.Abstractions;

/// <summary>
/// The raw bytes of a conflicted file's index stages: the common ancestor (1), the current side
/// (2) and the incoming side (3). A null stage is missing (added on both sides, or deleted).
/// </summary>
public sealed record ConflictStageContents(byte[]? Base, byte[]? Ours, byte[]? Theirs);
