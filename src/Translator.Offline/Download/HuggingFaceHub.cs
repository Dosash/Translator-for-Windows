using System.Text.Json;

namespace Translator.Offline;

/// <summary>A file of a repository at a specific commit. <see cref="Sha256"/> is the LFS oid (null for plain git files).</summary>
internal sealed record RemoteFile(string Path, long Size, string GitOid, string? Sha256)
{
    public string ContentId => Sha256 ?? GitOid;
}

internal sealed record RepoSnapshot(string Repo, string Commit, IReadOnlyDictionary<string, RemoteFile> Files, DateTimeOffset FetchedAt);

/// <summary>Minimal Hugging Face Hub client: resolve the current commit, list its files, build download URLs.</summary>
internal sealed class HuggingFaceHub
{
    public const string UserAgent = "Translator-for-Windows (+https://github.com/Dosash/Translator-for-Windows)";
    public static readonly Uri DefaultBaseUri = new("https://huggingface.co/");

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly int _maxAttempts;
    private readonly Func<int, TimeSpan> _retryDelay;

    public HuggingFaceHub(HttpClient http, Uri? baseUri = null, int maxAttempts = 4, Func<int, TimeSpan>? retryDelay = null)
    {
        _http = http;
        _baseUri = baseUri ?? DefaultBaseUri;
        _maxAttempts = maxAttempts;
        _retryDelay = retryDelay ?? Retry.DefaultDelay;
    }

    public async Task<RepoSnapshot> GetSnapshotAsync(string repo, CancellationToken cancellationToken)
    {
        var commit = await Retry.RunAsync(token => GetCommitAsync(repo, token), _maxAttempts, _retryDelay, cancellationToken)
            .ConfigureAwait(false);
        // Listing the tree at the resolved commit keeps sizes, hashes and download URLs consistent.
        var files = await Retry.RunAsync(token => GetTreeAsync(repo, commit, token), _maxAttempts, _retryDelay, cancellationToken)
            .ConfigureAwait(false);
        return new RepoSnapshot(repo, commit, files, DateTimeOffset.UtcNow);
    }

    public Uri GetDownloadUri(string repo, string revision, string path) =>
        new(_baseUri, $"{repo}/resolve/{revision}/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}");

    internal static HttpRequestMessage CreateRequest(HttpClient http, Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        }

        return request;
    }

    internal static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} from {response.RequestMessage?.RequestUri?.Host}.",
                null,
                response.StatusCode);
        }
    }

    private async Task<string> GetCommitAsync(string repo, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(new Uri(_baseUri, $"api/models/{repo}/revision/main"), cancellationToken).ConfigureAwait(false);
        return document.Document.RootElement.GetProperty("sha").GetString()
            ?? throw new InvalidDataException($"No commit sha for {repo}.");
    }

    private async Task<IReadOnlyDictionary<string, RemoteFile>> GetTreeAsync(string repo, string commit, CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, RemoteFile>(StringComparer.Ordinal);
        Uri? next = new(_baseUri, $"api/models/{repo}/tree/{commit}?recursive=true");
        while (next is not null)
        {
            using var page = await GetJsonAsync(next, cancellationToken).ConfigureAwait(false);
            foreach (var item in page.Document.RootElement.EnumerateArray())
            {
                if (item.GetProperty("type").GetString() != "file")
                {
                    continue;
                }

                var path = item.GetProperty("path").GetString()!;
                var size = item.GetProperty("size").GetInt64();
                string? sha256 = null;
                if (item.TryGetProperty("lfs", out var lfs) && lfs.ValueKind == JsonValueKind.Object)
                {
                    sha256 = lfs.GetProperty("oid").GetString();
                    if (lfs.TryGetProperty("size", out var lfsSize))
                    {
                        size = lfsSize.GetInt64();
                    }
                }

                files[path] = new RemoteFile(path, size, item.GetProperty("oid").GetString()!, sha256);
            }

            next = page.NextPage;
        }

        return files;
    }

    private async Task<JsonPage> GetJsonAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var request = CreateRequest(_http, uri);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
        EnsureSuccess(response);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
        return new JsonPage(document, ParseNextLink(response, uri));
    }

    private static Uri? ParseNextLink(HttpResponseMessage response, Uri current)
    {
        if (!response.Headers.TryGetValues("Link", out var values))
        {
            return null;
        }

        foreach (var part in values.SelectMany(v => v.Split(',')))
        {
            if (!part.Contains("rel=\"next\"", StringComparison.Ordinal))
            {
                continue;
            }

            var open = part.IndexOf('<');
            var close = part.IndexOf('>');
            if (open >= 0 && close > open)
            {
                return new Uri(current, part[(open + 1)..close]);
            }
        }

        return null;
    }

    private sealed record JsonPage(JsonDocument Document, Uri? NextPage) : IDisposable
    {
        public void Dispose() => Document.Dispose();
    }
}
