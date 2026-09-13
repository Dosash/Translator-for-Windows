using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace Translator.Core;

public sealed record UpdateCheckResult(bool UpdateAvailable, string Message, string? DownloadUrl);

/// <summary>Manual "check version" against GitHub Releases (Dosash/Translator-for-Windows).</summary>
public static class UpdateChecker
{
    private const string ReleasesUrl = "https://api.github.com/repos/Dosash/Translator-for-Windows/releases/latest";

    private static readonly Lazy<HttpClient> Shared = new(() => new HttpClient());

    public static Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
        CheckAsync(Shared.Value, cancellationToken);

    public static async Task<UpdateCheckResult> CheckAsync(HttpClient httpClient, CancellationToken cancellationToken = default)
    {
        var current = CurrentVersion();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Translator-for-Windows", current));

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // No releases published yet: treat as "up to date".
                return new UpdateCheckResult(false, L10n.Format("update.latest", current), null);
            }
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(false, L10n.Format("update.failed", $"HTTP {(int)response.StatusCode}"), null);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            var url = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() : null;

            if (string.IsNullOrWhiteSpace(tag))
            {
                return new UpdateCheckResult(false, L10n.Format("update.failed", L10n.T("error.bad.response")), null);
            }

            var latest = NormalizeVersion(tag);
            return IsNewer(latest, current)
                ? new UpdateCheckResult(true, L10n.Format("update.available", tag), url)
                : new UpdateCheckResult(false, L10n.Format("update.latest", current), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or OperationCanceledException)
        {
            return new UpdateCheckResult(false, L10n.Format("update.failed", ex.Message), null);
        }
    }

    /// <summary>Strips a leading "v"/"V" and any "+build metadata" suffix.</summary>
    internal static string NormalizeVersion(string raw)
    {
        var value = raw.Trim();
        var plusIndex = value.IndexOf('+');
        if (plusIndex >= 0)
        {
            value = value[..plusIndex];
        }
        return value.TrimStart('v', 'V');
    }

    internal static bool IsNewer(string latest, string current) => CompareVersions(latest, current) > 0;

    /// <summary>Numeric, dot/hyphen-segment comparison (like SemVer major.minor.patch, ignoring pre-release text).</summary>
    internal static int CompareVersions(string a, string b)
    {
        var partsA = ParseParts(a);
        var partsB = ParseParts(b);
        var length = Math.Max(partsA.Count, partsB.Count);
        for (var i = 0; i < length; i++)
        {
            var pa = i < partsA.Count ? partsA[i] : 0;
            var pb = i < partsB.Count ? partsB[i] : 0;
            var cmp = pa.CompareTo(pb);
            if (cmp != 0)
            {
                return cmp;
            }
        }
        return 0;
    }

    private static List<int> ParseParts(string version)
    {
        var parts = new List<int>();
        foreach (var chunk in NormalizeVersion(version).Split('.', '-'))
        {
            var digits = new string(chunk.TakeWhile(char.IsDigit).ToArray());
            parts.Add(int.TryParse(digits, out var n) ? n : 0);
        }
        return parts;
    }

    private static string CurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly();
        var info = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var raw = !string.IsNullOrWhiteSpace(info) ? info : assembly?.GetName().Version?.ToString() ?? "0.0.0";
        return NormalizeVersion(raw);
    }
}
