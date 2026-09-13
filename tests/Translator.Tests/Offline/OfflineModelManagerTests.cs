using System.IO;
using System.Net.Http;
using System.Text;
using Translator.Offline;

namespace Translator.Tests.Offline;

public sealed class OfflineModelManagerTests : IDisposable
{
    private readonly TemporaryDirectory _directory = new();
    private readonly FakeHub _hub = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task DownloadInstallsBothDirectionsAndReportsProgress()
    {
        using var manager = CreateManager();
        var observed = new List<OfflineLanguageState>();
        manager.StateChanged += (_, code) =>
        {
            if (code == "ru")
            {
                lock (observed)
                {
                    observed.Add(manager.GetState("ru"));
                }
            }
        };
        var reports = new List<OfflineDownloadProgress>();
        var expectedTotal = _hub.SizeOf(OfflineModelCatalog.GetModel("opus-mt-ru-en")) + _hub.SizeOf(OfflineModelCatalog.GetModel("opus-mt-en-ru"));

        await manager.DownloadAsync("ru", new SynchronousProgress<OfflineDownloadProgress>(reports.Add));

        Assert.Equal(OfflineLanguageState.Installed, manager.GetState("ru"));
        Assert.Equal([OfflineLanguageState.Downloading, OfflineLanguageState.Installed], observed);
        Assert.All(reports, r => Assert.Equal("ru", r.LanguageCode));
        Assert.Equal(expectedTotal, reports[^1].TotalBytes);
        Assert.Equal(expectedTotal, reports[^1].BytesReceived);
        Assert.Equal(expectedTotal, manager.GetInstalledSizeBytes("ru"));
        Assert.Equal(0, manager.GetDownloadSizeBytes("ru"));
        Assert.True(manager.IsPairReady("ru", "en"));
        Assert.True(manager.IsPairReady("en", "ru"));
        Assert.False(manager.IsPairReady("ru", "de"));
        Assert.True(File.Exists(Path.Combine(_directory.Path, "opus-mt-en-ru", "onnx", "decoder_model_merged_quantized.onnx")));
        Assert.Empty(Directory.GetFiles(_directory.Path, "*.partial", SearchOption.AllDirectories));

        using var reloaded = CreateManager();
        Assert.Equal(OfflineLanguageState.Installed, reloaded.GetState("ru"));
    }

    [Fact]
    public async Task DownloadSizesComeFromTheHubAfterRefresh()
    {
        using var manager = CreateManager();
        var estimate = OfflineModelCatalog.GetModel("opus-mt-es-en").EstimatedSizeBytes + OfflineModelCatalog.GetModel("opus-mt-en-es").EstimatedSizeBytes;
        Assert.Equal(estimate, manager.GetDownloadSizeBytes("es"));

        await manager.RefreshDownloadSizesAsync();

        var exact = _hub.SizeOf(OfflineModelCatalog.GetModel("opus-mt-es-en")) + _hub.SizeOf(OfflineModelCatalog.GetModel("opus-mt-en-es"));
        Assert.Equal(exact, manager.GetDownloadSizeBytes("es"));
    }

    [Fact]
    public async Task RemoveKeepsModelsSharedWithAnotherInstalledLanguage()
    {
        using var manager = CreateManager();
        var (sharedModel, first, second) = FindSharedModel();
        await manager.DownloadAsync(first);
        await manager.DownloadAsync(second);
        var sharedDirectory = Path.Combine(_directory.Path, sharedModel);

        await manager.RemoveAsync(first);

        Assert.Equal(OfflineLanguageState.NotInstalled, manager.GetState(first));
        Assert.Equal(OfflineLanguageState.Installed, manager.GetState(second));
        Assert.True(Directory.Exists(sharedDirectory));
        Assert.False(Directory.Exists(Path.Combine(_directory.Path, OfflineModelCatalog.FindLanguage(first)!.ToEnglishModelId)));

        await manager.RemoveAsync(second);

        Assert.False(Directory.Exists(sharedDirectory));
        Assert.Equal(OfflineLanguageState.NotInstalled, manager.GetState(second));
    }

    [Fact]
    public async Task UpdateCheckFlagsChangedFilesAndDownloadReplacesOnlyThem()
    {
        using var manager = CreateManager();
        await manager.DownloadAsync("de");
        Assert.Empty(await manager.CheckForUpdatesAsync());

        var model = OfflineModelCatalog.GetModel("opus-mt-en-de");
        _hub.SetFile(model.Repo, model.EncoderPath, Encoding.UTF8.GetBytes("new encoder weights"));

        Assert.Equal(["de"], await manager.CheckForUpdatesAsync());
        Assert.Equal(OfflineLanguageState.UpdateAvailable, manager.GetState("de"));
        Assert.True(manager.IsPairReady("de", "en"));
        using (var reloaded = CreateManager())
        {
            Assert.Equal(OfflineLanguageState.UpdateAvailable, reloaded.GetState("de"));
        }

        var downloadsBefore = _hub.FileDownloads;
        await manager.DownloadAsync("de");

        Assert.Equal(downloadsBefore + 1, _hub.FileDownloads);
        Assert.Equal(OfflineLanguageState.Installed, manager.GetState("de"));
        Assert.Equal("new encoder weights", File.ReadAllText(Path.Combine(_directory.Path, model.Id, "onnx", "encoder_model_quantized.onnx")));
        Assert.Empty(await manager.CheckForUpdatesAsync());
    }

    [Fact]
    public async Task InterruptedDownloadResumesWithoutRefetchingCompleteFiles()
    {
        using (var manager = CreateManager())
        {
            await manager.DownloadAsync("fr");
        }

        var model = OfflineModelCatalog.GetModel("opus-mt-en-fr");
        var modelDirectory = Path.Combine(_directory.Path, model.Id);
        File.Delete(Path.Combine(modelDirectory, ModelManifest.FileName));
        File.Delete(ModelInstaller.GetLocalPath(modelDirectory, model.DecoderPath));

        using var restarted = CreateManager();
        Assert.Equal(OfflineLanguageState.NotInstalled, restarted.GetState("fr"));
        var downloadsBefore = _hub.FileDownloads;
        await restarted.DownloadAsync("fr");

        Assert.Equal(downloadsBefore + 1, _hub.FileDownloads);
        Assert.Equal(OfflineLanguageState.Installed, restarted.GetState("fr"));
    }

    [Fact]
    public async Task FailedDownloadReportsAClearError()
    {
        using var manager = new OfflineModelManager(_directory.Path, new FakeHttpHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)).CreateClient(), FakeHub.BaseUri, _ => TimeSpan.Zero);

        var error = await Assert.ThrowsAsync<OfflineDownloadException>(() => manager.DownloadAsync("it"));

        Assert.Contains("404", error.Message);
        Assert.Equal(OfflineLanguageState.NotInstalled, manager.GetState("it"));
        await Assert.ThrowsAsync<ArgumentException>(() => manager.DownloadAsync("xx"));
    }

    [Fact]
    public async Task TranslateValidatesLanguagesBeforeLoadingModels()
    {
        using var manager = CreateManager();

        var missing = await Assert.ThrowsAsync<OfflineTranslationException>(() => manager.TranslateAsync("Привет, мир", "ru", "en"));
        Assert.Contains("ru", missing.Message);
        var detected = await Assert.ThrowsAsync<OfflineTranslationException>(() => manager.TranslateAsync("Привет, как дела?", null, "en"));
        Assert.Contains("ru", detected.Message);
        await Assert.ThrowsAsync<OfflineTranslationException>(() => manager.TranslateAsync("Hello", "en", "xx"));

        var same = await manager.TranslateAsync("Hello there", "en", "en");
        Assert.Equal(("Hello there", "en"), (same.Text, same.SourceCode));
        var unsure = await manager.TranslateAsync("OK", null, "en");
        Assert.Equal("en", unsure.SourceCode);

        Assert.True(manager.IsPairReady("en", "en"));
        Assert.False(manager.IsPairReady("auto", "ru"));
        Assert.Equal(OfflineLanguageState.Installed, manager.GetState("en"));
        Assert.Equal(OfflineLanguageState.Unsupported, manager.GetState("xx"));
    }

    private OfflineModelManager CreateManager() =>
        new(_directory.Path, _hub.Handler.CreateClient(), FakeHub.BaseUri, _ => TimeSpan.Zero);

    private static (string Model, string First, string Second) FindSharedModel()
    {
        foreach (var model in OfflineModelCatalog.Models)
        {
            var languages = OfflineModelCatalog.LanguagesUsingModel(model.Id);
            if (languages.Count > 1)
            {
                return (model.Id, languages[0], languages[1]);
            }
        }

        throw new InvalidOperationException("The catalog has no shared model.");
    }
}
