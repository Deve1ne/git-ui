namespace GitUi.Domain.Entities;

/// <param name="IsInitialized">The submodule has been cloned into its folder (git submodule update --init).</param>
/// <param name="IsModified">The checked-out commit differs from the one the parent repository records.</param>
public sealed record GitSubmodule(string Name, string Path, string Url, bool IsInitialized, bool IsModified);

/// <param name="IsMain">The repository's main working tree (cannot be removed).</param>
/// <param name="Branch">Checked-out branch, null when HEAD is detached.</param>
public sealed record GitWorktree(string Path, string? Branch, bool IsMain, bool IsLocked);
