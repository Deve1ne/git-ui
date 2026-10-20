namespace GitUi.Application.DTOs;

public enum RefLabelKind { LocalBranch, RemoteBranch, Tag }

public sealed record RefLabel(string Name, RefLabelKind Kind, bool IsHead);
