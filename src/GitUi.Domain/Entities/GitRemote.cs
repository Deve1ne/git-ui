namespace GitUi.Domain.Entities;

public sealed record GitRemote(string Name, string FetchUrl, string PushUrl);
