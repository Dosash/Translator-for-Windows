using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Translator.Core;

/// <summary>Any online translation engine, so <see cref="TranslatorModel"/> can be tested with a fake.</summary>
public interface IOnlineTranslator
{
    /// <summary><paramref name="source"/> is "auto" to let the engine detect the language.</summary>
    Task<OnlineTranslation> TranslateAsync(string text, string source, string target, CancellationToken cancellationToken = default);
}

public sealed record OnlineTranslation(string Text, string? DetectedSource);

public sealed class GoogleTranslateException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Online translation via the free, unofficial Google Translate endpoint. No API key, needs internet.
/// Text goes in the POST body so long inputs fit.
/// </summary>
public sealed class GoogleTranslateEngine : IOnlineTranslator
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly Lazy<HttpClient> Shared = new(() => new HttpClient());

    private readonly HttpClient _httpClient;

    public GoogleTranslateEngine(HttpClient? httpClient = null) => _httpClient = httpClient ?? Shared.Value;

    public async Task<OnlineTranslation> TranslateAsync(
        string text, string source, string target, CancellationToken cancellationToken = default)
    {
        var url = "https://translate.googleapis.com/translate_a/single" +
                  $"?client=gtx&sl={Uri.EscapeDataString(source)}&tl={Uri.EscapeDataString(target)}&dt=t";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("q", text)]),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded") { CharSet = "utf-8" };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GoogleTranslateException(L10n.T("error.bad.response"));
        }
        catch (HttpRequestException ex)
        {
            throw new GoogleTranslateException(L10n.T("error.bad.response"), ex);
        }

        using (response)
        {
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new GoogleTranslateException(L10n.Format("error.http", (int)response.StatusCode));
            }
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true);
            return ParseResponse(body);
        }
    }

    /// <summary>
    /// Parses the nested-array response: <c>[[["Hello","Привет",...], ...], null, "ru", ...]</c>.
    /// Segment translations are concatenated; the detected source (when sl=auto) is root[2].
    /// </summary>
    internal static OnlineTranslation ParseResponse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new GoogleTranslateException(L10n.T("error.bad.response"));
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0 ||
                root[0].ValueKind != JsonValueKind.Array)
            {
                throw new GoogleTranslateException(L10n.T("error.bad.response"));
            }

            var builder = new StringBuilder();
            foreach (var segment in root[0].EnumerateArray())
            {
                if (segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0 &&
                    segment[0].ValueKind == JsonValueKind.String)
                {
                    builder.Append(segment[0].GetString());
                }
            }

            if (builder.Length == 0)
            {
                throw new GoogleTranslateException(L10n.T("error.bad.response"));
            }

            string? detected = root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String
                ? root[2].GetString()
                : null;

            return new OnlineTranslation(builder.ToString(), detected);
        }
    }
}
