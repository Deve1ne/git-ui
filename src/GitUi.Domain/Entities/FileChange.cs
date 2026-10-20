namespace GitUi.Domain.Entities;

public enum FileChangeType { Added, Modified, Deleted, Renamed, Conflicted }

public sealed record FileChange(string Path, FileChangeType ChangeType, bool IsStaged);
