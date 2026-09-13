using Translator.Core;
using Translator.Offline;

namespace Translator.Tests.Core;

internal sealed class FakeOnlineTranslator : IOnlineTranslator
{
    public Func<string, string, string, Task<OnlineTranslation>>? Handler { get; set; }
    public List<(string Text, string Source, string Target)> Calls { get; } = [];

    public Task<OnlineTranslation> TranslateAsync(string text, string source, string target, CancellationToken cancellationToken = default)
    {
        Calls.Add((text, source, target));
        return Handler is not null
            ? Handler(text, source, target)
            : Task.FromException<OnlineTranslation>(new GoogleTranslateException("stub not configured"));
    }
}

internal sealed class FakeOfflineTranslator : IOfflineTranslator
{
    public bool PairReady { get; set; } = true;
    public Func<string, string?, string, Task<OfflineTranslation>>? Handler { get; set; }
    public List<(string Text, string? Source, string Target)> Calls { get; } = [];

    public bool IsPairReady(string sourceCode, string targetCode) => PairReady;

    public Task<OfflineTranslation> TranslateAsync(
        string text, string? sourceCode, string targetCode, CancellationToken cancellationToken = default)
    {
        Calls.Add((text, sourceCode, targetCode));
        return Handler is not null
            ? Handler(text, sourceCode, targetCode)
            : Task.FromException<OfflineTranslation>(new OfflineTranslationException("stub not configured"));
    }
}

internal sealed class FakeOfflineStatusSource : IOfflineStatusSource
{
    public Dictionary<string, OfflineLanguageState> States { get; } = new();
    public Func<CancellationToken, Task<IReadOnlyList<string>>>? UpdatesHandler { get; set; }

    public event EventHandler<string>? StateChanged;

    public OfflineLanguageState GetState(string languageCode) =>
        States.TryGetValue(languageCode, out var state) ? state : OfflineLanguageState.NotInstalled;

    public Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken cancellationToken = default) =>
        UpdatesHandler?.Invoke(cancellationToken) ?? Task.FromResult<IReadOnlyList<string>>([]);

    public void RaiseStateChanged(string code) => StateChanged?.Invoke(this, code);
}

internal sealed class FakeSpeechService : ISpeechService
{
    public List<(string Text, string Code)> Calls { get; } = [];
    public string? ErrorToReturn { get; set; }
    public int StopCount { get; private set; }

    public Task<string?> SpeakAsync(string text, string googleCode)
    {
        Calls.Add((text, googleCode));
        return Task.FromResult(ErrorToReturn);
    }

    public void Stop() => StopCount++;
}
