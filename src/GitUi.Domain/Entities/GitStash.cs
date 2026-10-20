namespace GitUi.Domain.Entities;

public sealed record GitStash(int Index, string Message, string Sha, DateTimeOffset Date);
