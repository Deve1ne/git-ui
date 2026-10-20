using GitUi.Domain.ValueObjects;

namespace GitUi.Application.DTOs;

public sealed record CommitGraphRow(CommitGraphNode Node, IReadOnlyList<RefLabel> Labels);
