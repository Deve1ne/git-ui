namespace GitUi.Application.Abstractions;

/// <summary>Side of a conflicted merge: the checked-out branch (ours, "current") or the merged-in one (theirs, "incoming").</summary>
public enum ConflictSide { Ours, Theirs }
