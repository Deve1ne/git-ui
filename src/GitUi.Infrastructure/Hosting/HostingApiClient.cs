using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GitUi.Application.Hosting;

namespace GitUi.Infrastructure.Hosting;

/// <summary>
/// What the four REST clients share: JSON requests, the authentication schemes to try, paging,
/// and errors turned into <see cref="HostingException"/> messages a user can act on.
/// </summary>
public abstract class HostingApiClient : IHostingClient
{
    /// <summary>Upper bound on pages read from a list, so a huge organization cannot hang the UI.</summary>
    protected const int MaxPages = 10;

    private readonly HttpClient _http;

    protected HostingApiClient(HttpClient http) => _http = http;

    public abstract HostingProvider Provider { get; }

    public abstract Task<HostingUser> GetCurrentUserAsync(HostingSession session, CancellationToken cancellationToken = default);

    public abstract Task<IReadOnlyList<HostedRepository>> GetRepositoriesAsync(HostingSession session, CancellationToken cancellationToken = default);

    public abstract Task<IReadOnlyList<PullRequest>> GetPullRequestsAsync(HostingSession session, RemoteRepositoryRef repository, CancellationToken cancellationToken = default);

    public abstract Task<string?> GetDefaultBranchAsync(HostingSession session, RemoteRepositoryRef repository, CancellationToken cancellationToken = default);

    public abstract Task<PullRequest> CreatePullRequestAsync(HostingSession session, RemoteRepositoryRef repository, NewPullRequest request, CancellationToken cancellationToken = default);

    /// <summary>The Authorization headers to try in order: the next one is tried when the service answers 401.</summary>
    protected abstract IReadOnlyList<AuthenticationHeaderValue> AuthorizationFor(HostingSession session);

    /// <summary>Headers a service wants on every request (API version...).</summary>
    protected virtual void AddHeaders(HttpRequestMessage request)
    {
    }

    /// <summary>The URL of the next page of a list, or null on the last page.</summary>
    protected virtual string? NextPage(HttpResponseMessage response, JsonElement body) => null;

    protected static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    protected static AuthenticationHeaderValue Basic(string user, string password) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));

    protected async Task<JsonElement> GetAsync(HostingSession session, string url, CancellationToken cancellationToken) =>
        (await SendAsync(session, HttpMethod.Get, url, null, cancellationToken)).Body;

    protected async Task<JsonElement> PostAsync(HostingSession session, string url, object body, CancellationToken cancellationToken) =>
        (await SendAsync(session, HttpMethod.Post, url, body, cancellationToken)).Body;

    /// <summary>Reads every page of a list (up to <see cref="MaxPages"/>); <paramref name="items"/> picks the items out of a page.</summary>
    protected async Task<List<T>> GetPagedAsync<T>(
        HostingSession session,
        string url,
        Func<JsonElement, IEnumerable<JsonElement>> items,
        Func<JsonElement, T> map,
        CancellationToken cancellationToken)
    {
        var result = new List<T>();
        string? next = url;
        for (var page = 0; next is not null && page < MaxPages; page++)
        {
            var (body, nextUrl) = await SendAsync(session, HttpMethod.Get, next, null, cancellationToken);
            result.AddRange(items(body).Select(map));
            next = nextUrl;
        }

        return result;
    }

    private async Task<(JsonElement Body, string? NextUrl)> SendAsync(
        HostingSession session,
        HttpMethod method,
        string url,
        object? body,
        CancellationToken cancellationToken)
    {
        var schemes = AuthorizationFor(session);
        for (var i = 0; ; i++)
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = schemes[i];
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("GitUi", "1.0"));
            AddHeaders(request);
            if (body is not null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new HostingException($"Could not reach {Provider.DisplayName()}: {ex.Message}", null, ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new HostingException($"{Provider.DisplayName()} did not answer in time.", null, ex);
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                if ((int)response.StatusCode == 401 && i + 1 < schemes.Count)
                {
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new HostingException(DescribeError((int)response.StatusCode, text), (int)response.StatusCode);
                }

                JsonElement json;
                try
                {
                    json = string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
                }
                catch (JsonException ex)
                {
                    // Azure DevOps answers a bad PAT with its HTML sign-in page and a 203.
                    throw new HostingException($"{Provider.DisplayName()} did not answer with JSON: the credential was probably refused.", (int)response.StatusCode, ex);
                }

                return (json, NextPage(response, json));
            }
        }
    }

    private string DescribeError(int status, string body)
    {
        var detail = ErrorDetail(body);
        var suffix = detail is null ? string.Empty : $": {detail}";
        var name = Provider.DisplayName();
        return status switch
        {
            401 => $"{name} refused the credential (401){suffix}. The token may be wrong or expired.",
            403 => $"{name} denied the request (403){suffix}. The token may lack a permission (scope).",
            404 => $"{name} found nothing there (404){suffix}. The repository may not exist, or this account cannot see it.",
            _ => $"{name} answered {status}{suffix}.",
        };
    }

    /// <summary>The error text the services put in their JSON bodies, whatever its shape.</summary>
    private static string? ErrorDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var message = Str(root, "message") ?? Str(root, "error", "message") ?? Str(root, "error_description") ?? Str(root, "errorMessage");
            if (message is null && root.TryGetProperty("message", out var nested) && nested.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                message = nested.ToString();
            }

            message ??= Str(root, "error");
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                var details = errors.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.Object ? Str(e, "message") ?? Str(e, "code") : e.ToString())
                    .Where(d => !string.IsNullOrEmpty(d));
                var joined = string.Join("; ", details);
                if (joined.Length > 0)
                {
                    message = message is null ? joined : $"{message} ({joined})";
                }
            }

            return message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A string property along <paramref name="path"/>, or null when any step is missing.</summary>
    protected static string? Str(JsonElement element, params string[] path)
    {
        foreach (var name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
            {
                return null;
            }
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null,
        };
    }

    protected static bool Bool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    protected static long Long(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;

    protected static DateTimeOffset? Date(JsonElement element, string name) =>
        Str(element, name) is { } text && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date)
            ? date
            : null;

    protected static IEnumerable<JsonElement> Items(JsonElement element, string? property = null)
    {
        if (property is not null)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out element))
            {
                return Enumerable.Empty<JsonElement>();
            }
        }

        return element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().ToList() : Enumerable.Empty<JsonElement>();
    }

    /// <summary>Next page from an RFC 5988 Link header (GitHub, GitLab).</summary>
    protected static string? NextFromLinkHeader(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values))
        {
            return null;
        }

        foreach (var part in string.Join(",", values).Split(','))
        {
            var pieces = part.Split(';');
            if (pieces.Length >= 2 && pieces.Skip(1).Any(p => p.Trim() == "rel=\"next\""))
            {
                return pieces[0].Trim().TrimStart('<').TrimEnd('>');
            }
        }

        return null;
    }

    protected static string Escape(string value) => Uri.EscapeDataString(value);
}
