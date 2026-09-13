using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Translator.Offline;

namespace Translator.Tests.Offline;

public sealed class FileDownloaderTests : IDisposable
{
    private static readonly Uri FileUri = new("https://hub.test/org/repo/resolve/abc/onnx/model.onnx");
    private readonly TemporaryDirectory _directory = new();
    private readonly byte[] _content = RandomBytes(200_000, seed: 1);

    public FileDownloaderTests() => Directory.CreateDirectory(_directory.Path);

    private string Destination => Path.Combine(_directory.Path, "onnx", "model.onnx");

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task ResumesPartialFileWithRangeRequest()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        File.WriteAllBytes(FileDownloader.GetPartialPath(Destination), _content[..50_000]);
        var handler = new FakeHttpHandler(request => FakeHttpHandler.ServeBytes(request, _content));
        var reported = new List<long>();

        var partial = await CreateDownloader(handler).DownloadToPartialAsync(FileUri, Destination, Lfs(_content), reported.Add, CancellationToken.None);

        Assert.Equal(Destination + ".partial", partial);
        Assert.Equal(_content, File.ReadAllBytes(partial));
        Assert.Equal(50_000L, Assert.Single(handler.Requests).Range?.Ranges.Single().From);
        Assert.Equal(50_000, reported[0]);
        Assert.Equal(_content.Length, reported[^1]);
    }

    [Fact]
    public async Task ServerIgnoringRangeRestartsTheFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        File.WriteAllBytes(FileDownloader.GetPartialPath(Destination), RandomBytes(50_000, seed: 9));
        var handler = new FakeHttpHandler(request => FakeHttpHandler.ServeBytes(request, _content, honorRange: false));

        var partial = await CreateDownloader(handler).DownloadToPartialAsync(FileUri, Destination, Lfs(_content), null, CancellationToken.None);

        Assert.Equal(_content, File.ReadAllBytes(partial));
    }

    [Fact]
    public async Task TruncatedResponseIsResumedOnRetry()
    {
        var calls = 0;
        var handler = new FakeHttpHandler(request =>
            Interlocked.Increment(ref calls) == 1
                ? FakeHttpHandler.ServeBytes(request, _content, truncateAt: 120_000)
                : FakeHttpHandler.ServeBytes(request, _content));

        var partial = await CreateDownloader(handler).DownloadToPartialAsync(FileUri, Destination, Lfs(_content), null, CancellationToken.None);

        Assert.Equal(_content, File.ReadAllBytes(partial));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(120_000L, handler.Requests[1].Range?.Ranges.Single().From);
    }

    [Fact]
    public async Task ChecksumMismatchIsRetriedThenFails()
    {
        var handler = new FakeHttpHandler(request => FakeHttpHandler.ServeBytes(request, _content));
        var wrong = Lfs(RandomBytes(_content.Length, seed: 2));

        await Assert.ThrowsAsync<DownloadVerificationException>(() =>
            CreateDownloader(handler).DownloadToPartialAsync(FileUri, Destination, wrong, null, CancellationToken.None));

        Assert.Equal(4, handler.Requests.Count);
        Assert.False(File.Exists(FileDownloader.GetPartialPath(Destination)));
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task ServerErrorIsRetried()
    {
        var calls = 0;
        var handler = new FakeHttpHandler(request =>
            Interlocked.Increment(ref calls) == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : FakeHttpHandler.ServeBytes(request, _content));

        var partial = await CreateDownloader(handler).DownloadToPartialAsync(FileUri, Destination, Lfs(_content), null, CancellationToken.None);

        Assert.Equal(_content, File.ReadAllBytes(partial));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task NotFoundFailsWithoutRetrying()
    {
        var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateDownloader(handler).DownloadToPartialAsync(FileUri, Destination, Lfs(_content), null, CancellationToken.None));

        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CancellationStopsTheDownload()
    {
        var handler = new FakeHttpHandler(request => FakeHttpHandler.ServeBytes(request, _content));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateDownloader(handler).DownloadToPartialAsync(FileUri, Destination, Lfs(_content), null, cancelled.Token));
    }

    [Fact]
    public async Task PlainFilesAreVerifiedWithTheGitBlobHash()
    {
        var path = Path.Combine(_directory.Path, "vocab.json");
        byte[] content = "{\"</s>\": 0}"u8.ToArray();
        File.WriteAllBytes(path, content);

        Assert.True(await FileDownloader.MatchesAsync(path, new RemoteFile("vocab.json", content.Length, FakeHub.GitOid(content), null), CancellationToken.None));
        Assert.False(await FileDownloader.MatchesAsync(path, new RemoteFile("vocab.json", content.Length, new string('0', 40), null), CancellationToken.None));
        Assert.False(await FileDownloader.MatchesAsync(path, new RemoteFile("vocab.json", content.Length + 1, FakeHub.GitOid(content), null), CancellationToken.None));
    }

    private static FileDownloader CreateDownloader(FakeHttpHandler handler) =>
        new(handler.CreateClient(), retryDelay: _ => TimeSpan.Zero);

    private static RemoteFile Lfs(byte[] content) =>
        new("onnx/model.onnx", content.Length, "pointer-oid", Convert.ToHexStringLower(SHA256.HashData(content)));

    private static byte[] RandomBytes(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }
}
