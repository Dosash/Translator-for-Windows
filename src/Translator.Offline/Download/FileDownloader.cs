using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Translator.Offline;

/// <summary>Thrown when a downloaded file fails its size or hash check; retried like a network error.</summary>
internal sealed class DownloadVerificationException(string message) : IOException(message);

/// <summary>
/// Downloads one file into "&lt;destination&gt;.partial" with HTTP Range resume, stall detection, retries and
/// size + hash verification. Moving the verified file into place is left to the caller so a whole model can be
/// swapped at once.
/// </summary>
internal sealed class FileDownloader
{
    public const string PartialSuffix = ".partial";

    private readonly HttpClient _http;
    private readonly int _maxAttempts;
    private readonly Func<int, TimeSpan> _retryDelay;
    private readonly TimeSpan _stallTimeout;

    /// <param name="maxAttempts">One try plus three retries by default.</param>
    public FileDownloader(HttpClient http, int maxAttempts = 4, Func<int, TimeSpan>? retryDelay = null, TimeSpan? stallTimeout = null)
    {
        _http = http;
        _maxAttempts = maxAttempts;
        _retryDelay = retryDelay ?? Retry.DefaultDelay;
        _stallTimeout = stallTimeout ?? TimeSpan.FromSeconds(30);
    }

    public static string GetPartialPath(string destination) => destination + PartialSuffix;

    /// <summary>Returns the path of the verified partial file. <paramref name="progress"/> receives bytes present so far.</summary>
    public Task<string> DownloadToPartialAsync(
        Uri uri,
        string destination,
        RemoteFile expected,
        Action<long>? progress,
        CancellationToken cancellationToken)
    {
        var partial = GetPartialPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(partial))!);
        return Retry.RunAsync(
            async token =>
            {
                await DownloadOnceAsync(uri, partial, expected, progress, token).ConfigureAwait(false);
                await VerifyAsync(partial, expected, token).ConfigureAwait(false);
                return partial;
            },
            _maxAttempts,
            _retryDelay,
            cancellationToken);
    }

    /// <summary>True when the file exists with the expected size and hash (SHA-256 for LFS, git blob SHA-1 otherwise).</summary>
    public static async Task<bool> MatchesAsync(string path, RemoteFile expected, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != expected.Size)
        {
            return false;
        }

        if (expected.Sha256 is { } sha256)
        {
            var actual = await HashAsync(path, HashAlgorithmName.SHA256, null, cancellationToken).ConfigureAwait(false);
            return string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase);
        }

        if (expected.GitOid.Length == 40)
        {
            var actual = await HashAsync(path, HashAlgorithmName.SHA1, $"blob {info.Length}\0", cancellationToken).ConfigureAwait(false);
            return string.Equals(actual, expected.GitOid, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private async Task DownloadOnceAsync(Uri uri, string partial, RemoteFile expected, Action<long>? progress, CancellationToken cancellationToken)
    {
        var existing = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (existing > expected.Size)
        {
            File.Delete(partial);
            existing = 0;
        }

        if (existing == expected.Size && existing > 0)
        {
            progress?.Invoke(existing);
            return;
        }

        using var request = HuggingFaceHub.CreateRequest(_http, uri);
        if (existing > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Delete(partial);
            throw new DownloadVerificationException("The server rejected the resume range.");
        }

        HuggingFaceHub.EnsureSuccess(response);

        var append = false;
        if (existing > 0 && response.StatusCode == HttpStatusCode.PartialContent)
        {
            if (response.Content.Headers.ContentRange?.From != existing)
            {
                File.Delete(partial);
                throw new DownloadVerificationException("The server returned an unexpected range.");
            }

            append = true;
        }

        var written = append ? existing : 0;
        progress?.Invoke(written);

        var buffer = ArrayPool<byte>.Shared.Rent(1 << 16);
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var file = new FileStream(partial, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            while (true)
            {
                stall.CancelAfter(_stallTimeout);
                int read;
                try
                {
                    read = await body.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new IOException("The download stalled.");
                }

                if (read == 0)
                {
                    break;
                }

                if (written + read > expected.Size)
                {
                    throw new DownloadVerificationException("The server sent more data than expected.");
                }

                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                written += read;
                progress?.Invoke(written);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task VerifyAsync(string partial, RemoteFile expected, CancellationToken cancellationToken)
    {
        var length = new FileInfo(partial).Length;
        if (length < expected.Size)
        {
            // Keep the partial file: the next attempt resumes from here.
            throw new IOException($"The connection closed early ({length} of {expected.Size} bytes).");
        }

        if (!await MatchesAsync(partial, expected, cancellationToken).ConfigureAwait(false))
        {
            File.Delete(partial);
            throw new DownloadVerificationException($"Checksum mismatch for {expected.Path}.");
        }
    }

    private static async Task<string> HashAsync(string path, HashAlgorithmName algorithm, string? prefix, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(algorithm);
        if (prefix is not null)
        {
            hash.AppendData(Encoding.ASCII.GetBytes(prefix));
        }

        var buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
