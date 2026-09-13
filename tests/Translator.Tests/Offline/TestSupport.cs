using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Translator.Offline;

namespace Translator.Tests.Offline;

internal sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "translator-offline-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}

internal sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    private readonly object _sync = new();
    private readonly List<(Uri Uri, RangeHeaderValue? Range)> _requests = [];

    public IReadOnlyList<(Uri Uri, RangeHeaderValue? Range)> Requests
    {
        get
        {
            lock (_sync)
            {
                return _requests.ToList();
            }
        }
    }

    public static HttpResponseMessage ServeBytes(HttpRequestMessage request, byte[] content, bool honorRange = true, int? truncateAt = null)
    {
        var from = honorRange && request.Headers.Range?.Ranges.FirstOrDefault()?.From is { } start ? (int)start : 0;
        var end = Math.Min(truncateAt ?? content.Length, content.Length);
        var response = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content, from, end - from),
        };
        if (from > 0)
        {
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, content.Length - 1, content.Length);
        }

        return response;
    }

    public HttpClient CreateClient() => new(this, disposeHandler: false);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _requests.Add((request.RequestUri!, request.Headers.Range));
        }

        var response = responder(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}

/// <summary>In-memory Hugging Face Hub: revision, recursive tree and resolve endpoints for the real catalog repos.</summary>
internal sealed class FakeHub
{
    public static readonly Uri BaseUri = new("https://hub.test/");

    private readonly object _sync = new();
    private readonly Dictionary<string, SortedDictionary<string, byte[]>> _repos = new(StringComparer.Ordinal);
    private int _fileDownloads;

    public FakeHub()
    {
        Handler = new FakeHttpHandler(Respond);
        foreach (var file in OfflineModelCatalog.Models.SelectMany(m => m.Files))
        {
            SetFile(file.Repo, file.Path, Encoding.UTF8.GetBytes($"{file.Repo}/{file.Path} v1"));
        }
    }

    public FakeHttpHandler Handler { get; }

    public int FileDownloads => Volatile.Read(ref _fileDownloads);

    public void SetFile(string repo, string path, byte[] content)
    {
        lock (_sync)
        {
            if (!_repos.TryGetValue(repo, out var files))
            {
                _repos[repo] = files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            }

            files[path] = content;
        }
    }

    public long SizeOf(OfflineModelInfo model)
    {
        lock (_sync)
        {
            return model.Files.Sum(f => (long)_repos[f.Repo][f.Path].Length);
        }
    }

    public static string GitOid(byte[] content) =>
        Convert.ToHexStringLower(SHA1.HashData([.. Encoding.ASCII.GetBytes($"blob {content.Length}\0"), .. content]));

    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath).TrimStart('/');
        lock (_sync)
        {
            if (path.StartsWith("api/models/", StringComparison.Ordinal))
            {
                var rest = path["api/models/".Length..];
                if (rest.IndexOf("/revision/", StringComparison.Ordinal) is var revision and > 0 && _repos.ContainsKey(rest[..revision]))
                {
                    return Json(new Dictionary<string, object> { ["sha"] = CommitOf(rest[..revision]) });
                }

                if (rest.IndexOf("/tree/", StringComparison.Ordinal) is var tree and > 0 && _repos.TryGetValue(rest[..tree], out var repoFiles))
                {
                    return Json(repoFiles.Select(pair => TreeEntry(pair.Key, pair.Value)).ToList());
                }
            }

            if (path.IndexOf("/resolve/", StringComparison.Ordinal) is var resolve and > 0 && _repos.TryGetValue(path[..resolve], out var files))
            {
                var afterRevision = path[(resolve + "/resolve/".Length)..];
                var filePath = afterRevision[(afterRevision.IndexOf('/') + 1)..];
                if (files.TryGetValue(filePath, out var content))
                {
                    Interlocked.Increment(ref _fileDownloads);
                    return FakeHttpHandler.ServeBytes(request, content);
                }
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private string CommitOf(string repo) =>
        Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(string.Join('|', _repos[repo].Select(p => p.Key + GitOid(p.Value))))));

    private static Dictionary<string, object> TreeEntry(string path, byte[] content)
    {
        var entry = new Dictionary<string, object>
        {
            ["type"] = "file",
            ["oid"] = GitOid(Encoding.UTF8.GetBytes("pointer " + path + content.Length)),
            ["size"] = content.Length,
            ["path"] = path,
        };

        if (path.EndsWith(".onnx", StringComparison.Ordinal) || path.EndsWith(".spm", StringComparison.Ordinal))
        {
            entry["lfs"] = new Dictionary<string, object>
            {
                ["oid"] = Convert.ToHexStringLower(SHA256.HashData(content)),
                ["size"] = content.Length,
                ["pointerSize"] = 133,
            };
        }
        else
        {
            entry["oid"] = GitOid(content);
        }

        return entry;
    }

    private static HttpResponseMessage Json(object value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
}

/// <summary>Models downloaded locally (the app's models directory or TRANSLATOR_MODELS_DIR); CI has none.</summary>
internal static class LocalModels
{
    public static string Directory { get; } =
        Environment.GetEnvironmentVariable("TRANSLATOR_MODELS_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Translator", "models");

    public static bool Has(params string[] modelIds) =>
        modelIds.All(id => File.Exists(Path.Combine(Directory, id, "manifest.json")));
}

/// <summary>Skips the test unless the listed models are downloaded.</summary>
public sealed class RequiresModelsFactAttribute : FactAttribute
{
    public RequiresModelsFactAttribute(params string[] modelIds)
    {
        if (!LocalModels.Has(modelIds))
        {
            Skip = $"Offline models not downloaded: {string.Join(", ", modelIds)}";
        }
    }
}
