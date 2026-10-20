namespace GitUi.Domain.Entities;

public sealed record GitRepository(string Path, string CurrentBranch, bool IsDirty, string? HeadSha = null);
