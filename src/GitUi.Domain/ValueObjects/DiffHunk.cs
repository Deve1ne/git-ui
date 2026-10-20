using GitUi.Domain.ValueObjects;

namespace GitUi.Domain.ValueObjects;

public sealed record DiffHunk(int OldStart, int NewStart, IReadOnlyList<DiffLine> Lines);
