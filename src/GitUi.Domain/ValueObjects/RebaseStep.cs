namespace GitUi.Domain.ValueObjects;

public enum RebaseAction { Pick, Reword, Squash, Fixup, Drop }

/// <summary>One line of an interactive rebase: what to do with <see cref="Sha"/>.</summary>
/// <param name="NewMessage">The message for a <see cref="RebaseAction.Reword"/>.</param>
public sealed record RebaseStep(string Sha, RebaseAction Action, string? NewMessage = null);
