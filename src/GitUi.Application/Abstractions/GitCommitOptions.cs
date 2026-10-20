namespace GitUi.Application.Abstractions;

/// <param name="Amend">Replace the HEAD commit (message and content) instead of adding a new one.</param>
/// <param name="SkipHooks">Pass <c>--no-verify</c>: pre-commit and commit-msg hooks do not run.</param>
public sealed record GitCommitOptions(bool Amend = false, bool SkipHooks = false)
{
    public static GitCommitOptions Default { get; } = new();
}
