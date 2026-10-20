namespace GitUi.Application.Abstractions;

/// <summary>A multi-step git operation left in progress, usually stopped on conflicts.</summary>
public enum GitOperationState { None, Merge, Rebase, CherryPick, Revert }
