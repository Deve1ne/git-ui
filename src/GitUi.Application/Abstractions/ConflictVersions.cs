namespace GitUi.Application.Abstractions;

/// <summary>
/// Which index stages a conflicted file has. A missing side means the file was deleted on that
/// side (modify/delete conflict), which cannot be resolved line by line.
/// </summary>
public sealed record ConflictVersions(bool HasBase, bool HasOurs, bool HasTheirs)
{
    public bool IsDeleteConflict => !HasOurs || !HasTheirs;
}
