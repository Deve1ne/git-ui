namespace GitUi.Application.Abstractions;

/// <param name="Branch">Branch to check out after cloning; null for the remote's default branch.</param>
/// <param name="Depth">Shallow clone depth; null for full history.</param>
/// <param name="RecurseSubmodules">Also clone and check out submodules.</param>
public sealed record CloneOptions(string? Branch = null, int? Depth = null, bool RecurseSubmodules = false);
