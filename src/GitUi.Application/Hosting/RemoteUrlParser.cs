namespace GitUi.Application.Hosting;

/// <summary>
/// Recognizes remote URLs of the cloud hosting services, in their HTTPS and SSH forms:
/// <list type="bullet">
/// <item>github.com/owner/repo, git@github.com:owner/repo.git</item>
/// <item>gitlab.com/group/subgroup/repo (any depth)</item>
/// <item>dev.azure.com/org/project/_git/repo, org.visualstudio.com/[DefaultCollection/]project/_git/repo,
/// git@ssh.dev.azure.com:v3/org/project/repo</item>
/// <item>bitbucket.org/workspace/repo</item>
/// </list>
/// </summary>
public static class RemoteUrlParser
{
    public static RemoteRepositoryRef? Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || SplitHostAndPath(url.Trim()) is not { } parts)
        {
            return null;
        }

        var (host, path) = parts;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToList();
        if (segments.Count > 0)
        {
            segments[^1] = TrimGitSuffix(segments[^1]);
        }

        host = host.ToLowerInvariant();
        switch (host)
        {
            case "github.com" when segments.Count == 2:
                return new RemoteRepositoryRef(HostingProvider.GitHub, segments[0], segments[1]);
            case "gitlab.com" when segments.Count >= 2:
                return new RemoteRepositoryRef(HostingProvider.GitLab, string.Join('/', segments.Take(segments.Count - 1)), segments[^1]);
            case "bitbucket.org" when segments.Count == 2:
                return new RemoteRepositoryRef(HostingProvider.Bitbucket, segments[0], segments[1]);
            case "dev.azure.com" when segments.Count == 4 && segments[2] == "_git":
                return new RemoteRepositoryRef(HostingProvider.AzureDevOps, segments[0], segments[3], segments[1]);
            case "ssh.dev.azure.com" or "vs-ssh.visualstudio.com" when segments.Count == 4 && segments[0] == "v3":
                return new RemoteRepositoryRef(HostingProvider.AzureDevOps, segments[1], segments[3], segments[2]);
        }

        // Older Azure DevOps URLs: https://org.visualstudio.com/[DefaultCollection/]project/_git/repo
        if (host.EndsWith(".visualstudio.com", StringComparison.Ordinal))
        {
            var organization = host[..^".visualstudio.com".Length];
            var rest = segments.Count > 0 && segments[0].Equals("DefaultCollection", StringComparison.OrdinalIgnoreCase) ? segments.Skip(1).ToList() : segments;
            if (rest.Count == 3 && rest[1] == "_git")
            {
                return new RemoteRepositoryRef(HostingProvider.AzureDevOps, organization, rest[2], rest[0]);
            }
        }

        return null;
    }

    /// <summary>Host and path of "https://user@host/path", "ssh://git@host:22/path" or scp-like "git@host:path".</summary>
    private static (string Host, string Path)? SplitHostAndPath(string url)
    {
        if (url.Contains("://", StringComparison.Ordinal))
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0
                ? (uri.Host, uri.AbsolutePath)
                : null;
        }

        // scp-like syntax: [user@]host:path
        var colon = url.IndexOf(':');
        if (colon <= 0)
        {
            return null;
        }

        var host = url[..colon];
        var at = host.LastIndexOf('@');
        if (at >= 0)
        {
            host = host[(at + 1)..];
        }

        return host.Length == 0 ? null : (host, url[(colon + 1)..]);
    }

    private static string TrimGitSuffix(string name) =>
        name.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
