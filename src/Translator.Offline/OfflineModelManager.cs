using System.Collections.Concurrent;
using System.Net;

namespace Translator.Offline;

/// <summary>
/// Downloads, tracks and runs offline translation models (one language = a pair of
/// models "xx→en" and "en→xx"; English is the pivot). Thread-safe.
/// </summary>
public sealed class OfflineModelManager : IOfflineTranslator, IDisposable
{
    public const string BaseLanguage = OfflineModelCatalog.BaseLanguage;

    /// <summary>Source tokens per model call; longer sentences are split because Opus-MT degrades on long inputs.</summary>
    internal const int MaxSourceTokensPerCall = 160;

    private const int LoadedModelCapacity = 3;
    private static readonly TimeSpan IdleCheckInterval = TimeSpan.FromMinutes(1);

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly HuggingFaceHub _hub;
    private readonly ModelInstaller _installer;
    private readonly ModelCache<MarianModel> _models;
    private readonly ConcurrentDictionary<string, ModelManifest> _manifests = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RepoSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _modelLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ActiveDownload> _downloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly Timer _idleTimer;
    private int _disposed;

    public OfflineModelManager(string modelsDirectory, HttpClient? httpClient = null)
        : this(modelsDirectory, httpClient, hubBaseUri: null, retryDelay: null)
    {
    }

    internal OfflineModelManager(string modelsDirectory, HttpClient? httpClient, Uri? hubBaseUri, Func<int, TimeSpan>? retryDelay)
    {
        ModelsDirectory = modelsDirectory;
        _ownsHttp = httpClient is null;
        _http = httpClient ?? CreateHttpClient();
        _hub = new HuggingFaceHub(_http, hubBaseUri, retryDelay: retryDelay);
        _installer = new ModelInstaller(_hub, new FileDownloader(_http, retryDelay: retryDelay));
        _models = new ModelCache<MarianModel>(LoadedModelCapacity, LoadModel);
        LoadManifests();
        _idleTimer = new Timer(static state => ((OfflineModelManager)state!).TrimIdleModels(), this, IdleCheckInterval, IdleCheckInterval);
    }

    public string ModelsDirectory { get; }

    /// <summary>Languages that can be downloaded (every app language except English).</summary>
    public static IReadOnlyList<string> SupportedLanguages { get; } = OfflineModelCatalog.Languages.Select(l => l.Code).ToArray();

    /// <summary>Raised (on a background thread) with the language code whose state changed.</summary>
    public event EventHandler<string>? StateChanged;

    /// <summary>ONNX Runtime settings for models loaded from now on.</summary>
    internal InferenceSettings Inference { get; set; } = new(Math.Clamp(Environment.ProcessorCount / 2, 1, 4));

    /// <summary>Loaded models unused for this long are released.</summary>
    internal TimeSpan ModelIdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

    public OfflineLanguageState GetState(string languageCode)
    {
        var code = OfflineModelCatalog.NormalizeCode(languageCode);
        if (code == BaseLanguage)
        {
            return OfflineLanguageState.Installed;
        }

        if (OfflineModelCatalog.FindLanguage(code) is not { } language)
        {
            return OfflineLanguageState.Unsupported;
        }

        lock (_downloads)
        {
            if (_downloads.ContainsKey(language.Code))
            {
                return OfflineLanguageState.Downloading;
            }
        }

        var manifests = language.ModelIds.Select(id => _manifests.GetValueOrDefault(id)).ToList();
        if (manifests.Any(m => m is null))
        {
            return OfflineLanguageState.NotInstalled;
        }

        return manifests.Any(m => m!.UpdateAvailable) ? OfflineLanguageState.UpdateAvailable : OfflineLanguageState.Installed;
    }

    /// <summary>True when the models for this language are known to be of basic quality.</summary>
    public static bool IsBasicQuality(string languageCode) =>
        OfflineModelCatalog.FindLanguage(languageCode)?.IsBasicQuality ?? false;

    /// <summary>Bytes still to download (models shared with an installed language are not counted).</summary>
    public long GetDownloadSizeBytes(string languageCode)
    {
        if (OfflineModelCatalog.FindLanguage(languageCode) is not { } language)
        {
            return 0;
        }

        return language.ModelIds
            .Distinct()
            .Where(id => !_manifests.TryGetValue(id, out var manifest) || manifest.UpdateAvailable)
            .Sum(id => GetRemoteSize(OfflineModelCatalog.GetModel(id)));
    }

    public long GetInstalledSizeBytes(string languageCode)
    {
        if (OfflineModelCatalog.FindLanguage(languageCode) is not { } language)
        {
            return 0;
        }

        return language.ModelIds.Distinct().Sum(id => _manifests.TryGetValue(id, out var manifest) ? manifest.TotalSize : 0);
    }

    /// <summary>Fetches exact file sizes from the Hub so <see cref="GetDownloadSizeBytes"/> stops using estimates.</summary>
    public async Task RefreshDownloadSizesAsync(CancellationToken cancellationToken = default)
    {
        var repos = OfflineModelCatalog.Models.SelectMany(m => m.Repos).Distinct().ToList();
        try
        {
            await Parallel.ForEachAsync(
                repos,
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
                async (repo, token) => _snapshots[repo] = await _hub.GetSnapshotAsync(repo, token).ConfigureAwait(false))
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw new OfflineDownloadException($"Couldn't read model sizes: {DescribeError(ex)}.", ex);
        }
    }

    public async Task DownloadAsync(
        string languageCode,
        IProgress<OfflineDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var code = OfflineModelCatalog.NormalizeCode(languageCode);
        if (code == BaseLanguage)
        {
            return;
        }

        var language = OfflineModelCatalog.FindLanguage(code)
            ?? throw new ArgumentException($"Language '{languageCode}' is not supported offline.", nameof(languageCode));

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCancellation.Token);
        var download = new ActiveDownload(cancellation);
        lock (_downloads)
        {
            if (!_downloads.TryAdd(language.Code, download))
            {
                throw new InvalidOperationException($"Language '{language.Code}' is already downloading.");
            }
        }

        RaiseStateChanged([language.Code]);
        try
        {
            await DownloadLanguageAsync(language, progress, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw new OperationCanceledException("The download was cancelled.", null, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new OfflineDownloadException($"Couldn't download '{language.Code}': {DescribeError(ex)}.", ex);
        }
        finally
        {
            lock (_downloads)
            {
                _downloads.Remove(language.Code);
            }

            download.Completion.TrySetResult();
            RaiseStateChanged(AffectedLanguages(language));
        }
    }

    public async Task RemoveAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        if (OfflineModelCatalog.FindLanguage(languageCode) is not { } language)
        {
            return;
        }

        ActiveDownload? active;
        lock (_downloads)
        {
            _downloads.TryGetValue(language.Code, out active);
        }

        if (active is not null)
        {
            active.Cancel();
            await active.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var modelId in language.ModelIds.Distinct())
        {
            if (IsModelNeededByOtherLanguage(modelId, language.Code))
            {
                continue;
            }

            var gate = GetModelLock(modelId);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _manifests.TryRemove(modelId, out _);
                _models.Evict(modelId);
                var directory = GetModelDirectory(OfflineModelCatalog.GetModel(modelId));
                await Task.Run(() => DeleteDirectory(directory), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new OfflineDownloadException($"Couldn't remove '{language.Code}': {ex.Message}", ex);
            }
            finally
            {
                gate.Release();
            }
        }

        RaiseStateChanged(AffectedLanguages(language));
    }

    /// <summary>Returns installed languages whose remote model files changed since download.</summary>
    public async Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var installed = _manifests.Keys.Select(OfflineModelCatalog.FindModel).OfType<OfflineModelInfo>().ToList();
        var before = SupportedLanguages.ToDictionary(code => code, GetState);
        try
        {
            var snapshots = new Dictionary<string, RepoSnapshot>(StringComparer.Ordinal);
            foreach (var repo in installed.SelectMany(m => m.Repos).Distinct())
            {
                snapshots[repo] = _snapshots[repo] = await _hub.GetSnapshotAsync(repo, cancellationToken).ConfigureAwait(false);
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var model in installed)
            {
                if (_manifests.TryGetValue(model.Id, out var manifest))
                {
                    manifest.UpdateAvailable = manifest.GetChangedFiles(model, snapshots).Count > 0;
                    manifest.LastUpdateCheck = now;
                    TrySave(manifest, model);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw new OfflineDownloadException($"Couldn't check for model updates: {DescribeError(ex)}.", ex);
        }

        RaiseStateChanged(SupportedLanguages.Where(code => GetState(code) != before[code]));
        return SupportedLanguages.Where(code => GetState(code) == OfflineLanguageState.UpdateAvailable).ToList();
    }

    public bool IsPairReady(string sourceCode, string targetCode)
    {
        if (!IsLanguageReady(targetCode))
        {
            return false;
        }

        // With "auto" the source is only known after detection; TranslateAsync reports a missing language then.
        return string.Equals(sourceCode, "auto", StringComparison.OrdinalIgnoreCase) || IsLanguageReady(sourceCode);
    }

    public async Task<OfflineTranslation> TranslateAsync(
        string text,
        string? sourceCode,
        string targetCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var target = RequireSupported(targetCode);
        var source = sourceCode is null || string.Equals(sourceCode, "auto", StringComparison.OrdinalIgnoreCase)
            ? DetectLanguage(text) ?? BaseLanguage
            : RequireSupported(sourceCode);

        if (string.IsNullOrWhiteSpace(text) || source == target)
        {
            return new OfflineTranslation(text, source);
        }

        var plan = TranslationPlanner.Plan(source, target);
        foreach (var step in plan)
        {
            if (!_manifests.ContainsKey(step.Model.Id))
            {
                var missing = step.SourceCode == BaseLanguage ? step.TargetCode : step.SourceCode;
                throw new OfflineTranslationException($"Offline language '{missing}' is not downloaded.");
            }
        }

        try
        {
            var translated = await Task.Run(() => TranslateCore(text, plan, target, cancellationToken), cancellationToken).ConfigureAwait(false);
            return new OfflineTranslation(translated, source);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OfflineTranslationException))
        {
            throw new OfflineTranslationException($"Offline translation failed: {ex.Message}", ex);
        }
    }

    /// <summary>Best-effort language detection for offline mode; null when unsure.</summary>
    public static string? DetectLanguage(string text) => LanguageDetector.Detect(text);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _disposeCancellation.Cancel();
        _idleTimer.Dispose();
        _models.Dispose();
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    private string TranslateCore(string text, IReadOnlyList<TranslationStep> plan, string target, CancellationToken cancellationToken)
    {
        var leases = new List<ModelCache<MarianModel>.Lease>(plan.Count);
        try
        {
            foreach (var step in plan)
            {
                leases.Add(_models.Acquire(step.Model.Id, cancellationToken));
            }

            var sourceTokenizer = leases[0].Value.Tokenizer;
            var segmented = TextSegmenter.Split(text);
            var pieces = new List<TextSegment>(segmented.Segments.Count);
            foreach (var segment in segmented.Segments)
            {
                var parts = LongSegmentSplitter.Split(segment.Text, sourceTokenizer.CountTokens, MaxSourceTokensPerCall);
                for (var k = 0; k < parts.Count; k++)
                {
                    pieces.Add(k == parts.Count - 1 ? parts[k] with { Separator = segment.Separator } : parts[k]);
                }
            }

            var translated = new List<string>(pieces.Count);
            foreach (var piece in pieces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Numbers, symbols and emoji-only fragments tend to make the model hallucinate; keep them verbatim.
                if (!piece.Text.Any(char.IsLetter))
                {
                    translated.Add(piece.Text);
                    continue;
                }

                var current = piece.Text;
                for (var k = 0; k < plan.Count; k++)
                {
                    current = leases[k].Value.Translate(current, plan[k].TargetToken, cancellationToken);
                }

                translated.Add(current);
            }

            return TextSegmenter.Join(segmented with { Segments = pieces }, translated, target);
        }
        finally
        {
            leases.ForEach(lease => lease.Dispose());
        }
    }

    private async Task DownloadLanguageAsync(OfflineLanguageInfo language, IProgress<OfflineDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var models = language.ModelIds.Distinct().Select(OfflineModelCatalog.GetModel).ToList();
        var snapshots = new Dictionary<string, RepoSnapshot>(StringComparer.Ordinal);
        foreach (var repo in models.SelectMany(m => m.Repos).Distinct())
        {
            snapshots[repo] = _snapshots[repo] = await _hub.GetSnapshotAsync(repo, cancellationToken).ConfigureAwait(false);
        }

        var pending = models.Where(m => NeedsDownload(m, snapshots)).ToList();
        var reporter = new ProgressAggregator(language.Code, pending.Sum(m => m.Files.Sum(f => snapshots[f.Repo].Files.GetValueOrDefault(f.Path)?.Size ?? 0)), progress);
        foreach (var model in pending)
        {
            foreach (var file in model.Files)
            {
                // A resumed download should start at its real percentage, not at zero.
                var partial = new FileInfo(FileDownloader.GetPartialPath(ModelInstaller.GetLocalPath(GetModelDirectory(model), file.Path)));
                if (partial.Exists)
                {
                    reporter.Seed(file.Repo + "/" + file.Path, partial.Length);
                }
            }
        }

        reporter.Emit();

        foreach (var model in models)
        {
            var gate = GetModelLock(model.Id);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!NeedsDownload(model, snapshots))
                {
                    // Another language may have fetched a shared model meanwhile, or an update flag is stale.
                    if (_manifests.TryGetValue(model.Id, out var current) && current.UpdateAvailable)
                    {
                        current.UpdateAvailable = false;
                        TrySave(current, model);
                    }

                    continue;
                }

                var directory = GetModelDirectory(model);
                var existing = _manifests.GetValueOrDefault(model.Id) ?? ModelManifest.TryLoad(directory);
                var manifest = await _installer.InstallAsync(
                    model,
                    directory,
                    snapshots,
                    existing,
                    reporter.Report,
                    beforeReplace: () =>
                    {
                        _manifests.TryRemove(model.Id, out _);
                        _models.Evict(model.Id);
                    },
                    cancellationToken).ConfigureAwait(false);
                _manifests[model.Id] = manifest;
            }
            finally
            {
                gate.Release();
            }
        }

        reporter.Complete();
    }

    private bool NeedsDownload(OfflineModelInfo model, IReadOnlyDictionary<string, RepoSnapshot> snapshots) =>
        !_manifests.TryGetValue(model.Id, out var manifest) || manifest.GetChangedFiles(model, snapshots).Count > 0;

    private bool IsModelNeededByOtherLanguage(string modelId, string removedLanguage) =>
        OfflineModelCatalog.LanguagesUsingModel(modelId)
            .Where(code => code != removedLanguage)
            .Any(code => GetState(code) is OfflineLanguageState.Installed or OfflineLanguageState.UpdateAvailable or OfflineLanguageState.Downloading);

    private bool IsLanguageReady(string code)
    {
        var normalized = OfflineModelCatalog.NormalizeCode(code);
        return normalized == BaseLanguage
            || (OfflineModelCatalog.FindLanguage(normalized) is { } language && language.ModelIds.All(_manifests.ContainsKey));
    }

    private static string RequireSupported(string code)
    {
        var normalized = OfflineModelCatalog.NormalizeCode(code);
        return OfflineModelCatalog.IsSupported(normalized)
            ? normalized
            : throw new OfflineTranslationException($"Language '{code}' is not supported offline.");
    }

    private long GetRemoteSize(OfflineModelInfo model)
    {
        long total = 0;
        foreach (var file in model.Files)
        {
            if (!_snapshots.TryGetValue(file.Repo, out var snapshot) || !snapshot.Files.TryGetValue(file.Path, out var remote))
            {
                return model.EstimatedSizeBytes;
            }

            total += remote.Size;
        }

        return total;
    }

    private MarianModel LoadModel(string modelId)
    {
        var model = OfflineModelCatalog.GetModel(modelId);
        return MarianModel.Load(GetModelDirectory(model), model, Inference);
    }

    private void LoadManifests()
    {
        foreach (var model in OfflineModelCatalog.Models)
        {
            var directory = GetModelDirectory(model);
            if (ModelManifest.TryLoad(directory) is { } manifest && manifest.IsComplete(model, directory))
            {
                _manifests[model.Id] = manifest;
            }
        }
    }

    private void TrySave(ModelManifest manifest, OfflineModelInfo model)
    {
        try
        {
            manifest.Save(GetModelDirectory(model));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The flag still lives in memory; it is recomputed on the next check.
        }
    }

    private void TrimIdleModels()
    {
        try
        {
            _models.TrimIdle(ModelIdleTimeout);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private string GetModelDirectory(OfflineModelInfo model) => Path.Combine(ModelsDirectory, model.Id);

    private SemaphoreSlim GetModelLock(string modelId) => _modelLocks.GetOrAdd(modelId, static _ => new SemaphoreSlim(1, 1));

    private static IEnumerable<string> AffectedLanguages(OfflineLanguageInfo language) =>
        language.ModelIds.SelectMany(OfflineModelCatalog.LanguagesUsingModel).Prepend(language.Code).Distinct();

    private void RaiseStateChanged(IEnumerable<string> languageCodes)
    {
        foreach (var code in languageCodes.ToList())
        {
            try
            {
                StateChanged?.Invoke(this, code);
            }
            catch (Exception)
            {
                // A faulty subscriber must not break the download or removal that raised the event.
            }
        }
    }

    private static void DeleteDirectory(string directory)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }

                return;
            }
            catch (Exception ex) when (attempt < 5 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(200 * attempt);
            }
        }
    }

    private static string DescribeError(Exception exception) => exception switch
    {
        OfflineDownloadException { InnerException: { } inner } => DescribeError(inner),
        HttpRequestException { StatusCode: { } status } => $"the server returned HTTP {(int)status}",
        HttpRequestException ex => $"network error ({ex.Message})",
        DownloadVerificationException => "a downloaded file was corrupted",
        OperationCanceledException => "the connection timed out",
        _ => exception.Message.TrimEnd('.'),
    };

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.None,
        };

        // Downloads use their own stall timeout; a global timeout would cut off large files on slow links.
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private sealed class ActiveDownload(CancellationTokenSource cancellation)
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Cancel()
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private sealed class ProgressAggregator(string languageCode, long totalBytes, IProgress<OfflineDownloadProgress>? progress)
    {
        private readonly ConcurrentDictionary<string, long> _files = new(StringComparer.Ordinal);
        // Not long.MinValue: "now - last" would overflow and suppress every intermediate report.
        private long _lastReport;

        public void Seed(string file, long bytes) => _files[file] = bytes;

        public void Report(string file, long bytes)
        {
            _files[file] = bytes;
            var now = Environment.TickCount64;
            var last = Interlocked.Read(ref _lastReport);
            if (now - last >= 100 && Interlocked.CompareExchange(ref _lastReport, now, last) == last)
            {
                Emit();
            }
        }

        public void Emit() =>
            progress?.Report(new OfflineDownloadProgress(languageCode, Math.Min(_files.Values.Sum(), totalBytes), totalBytes));

        public void Complete() => progress?.Report(new OfflineDownloadProgress(languageCode, totalBytes, totalBytes));
    }
}
