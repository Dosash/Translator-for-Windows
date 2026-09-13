using System.IO;
using Translator.Core;
using Translator.Offline;

namespace Translator.Tests.Core;

public class TranslatorModelTests : IDisposable
{
    private readonly string _dir;

    public TranslatorModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "TranslatorTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
        L10n.Selected = AppUILanguage.En;
    }

    public void Dispose()
    {
        L10n.Selected = AppUILanguage.System;
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort cleanup */ }
    }

    private (TranslatorModel Model, SettingsStore Settings, HistoryStore History, FakeOnlineTranslator Online, FakeOfflineTranslator Offline, FakeOfflineStatusSource Status, FakeSpeechService Speech) Build(
        string source = "auto", string target = "ru")
    {
        var settings = new SettingsStore(Path.Combine(_dir, Guid.NewGuid() + "-settings.json"));
        settings.AutoTranslate = false;
        settings.SourceCode = source;
        settings.TargetCode = target;

        var history = new HistoryStore(Path.Combine(_dir, Guid.NewGuid() + "-history.json"));
        var online = new FakeOnlineTranslator();
        var offline = new FakeOfflineTranslator();
        var status = new FakeOfflineStatusSource();
        var speech = new FakeSpeechService();

        var model = new TranslatorModel(settings, history, online, offline, status, speech);
        return (model, settings, history, online, offline, status, speech);
    }

    [Fact]
    public async Task OnlineSuccess_SetsEngineGoogleAndRecordsHistory()
    {
        var (model, _, history, online, _, _, _) = Build(source: "en", target: "ru");
        online.Handler = (_, _, _) => Task.FromResult(new OnlineTranslation("привет", null));

        model.InputText = "hello";
        model.Translate();
        await model.CurrentTranslationTask!;

        Assert.Equal("привет", model.OutputText);
        Assert.Equal(EngineKind.Google, model.Engine);
        Assert.Null(model.ErrorMessage);
        Assert.False(model.IsTranslating);
        Assert.Single(history.Entries);
        Assert.Equal("привет", history.Entries[0].Output);
        Assert.Equal(EngineKind.Google, history.Entries[0].Engine);
    }

    [Fact]
    public async Task OnlineFailure_FallsBackToOfflineWhenReady()
    {
        var (model, _, history, online, offline, status, _) = Build(source: "en", target: "ru");
        online.Handler = (_, _, _) => Task.FromException<OnlineTranslation>(new GoogleTranslateException("boom"));
        offline.Handler = (_, _, _) => Task.FromResult(new OfflineTranslation("привет-офлайн", "en"));
        status.States["ru"] = OfflineLanguageState.Installed;

        model.InputText = "hello";
        model.Translate();
        await model.CurrentTranslationTask!;

        Assert.Equal("привет-офлайн", model.OutputText);
        Assert.Equal(EngineKind.Offline, model.Engine);
        Assert.Null(model.ErrorMessage);
        Assert.Single(history.Entries);
        Assert.Single(online.Calls);
        Assert.Single(offline.Calls);
    }

    [Fact]
    public async Task OfflineOnly_NeverCallsOnlineAndShowsNotice()
    {
        var (model, settings, _, online, offline, status, _) = Build(source: "en", target: "ru");
        settings.OfflineOnly = true;
        status.States["ru"] = OfflineLanguageState.Installed;
        offline.Handler = (_, _, _) => Task.FromResult(new OfflineTranslation("ok", "en"));

        model.InputText = "hello";
        model.Translate();
        await model.CurrentTranslationTask!;

        Assert.Empty(online.Calls);
        Assert.Single(offline.Calls);
        Assert.Equal(L10n.T("notice.offline.only"), model.Notice);
        Assert.Equal(EngineKind.Offline, model.Engine);
    }

    [Fact]
    public async Task TooLongText_SetsErrorAndCallsNoEngine()
    {
        var (model, _, _, online, offline, _, _) = Build();
        model.InputText = new string('a', TranslatorModel.MaxInputChars + 1);
        model.Translate();

        Assert.Equal(
            L10n.Format("error.too.long", TranslatorModel.MaxInputChars, TranslatorModel.MaxInputChars + 1),
            model.ErrorMessage);
        Assert.Empty(online.Calls);
        Assert.Empty(offline.Calls);
    }

    [Fact]
    public async Task OfflineOnlyMissingLanguages_SetsSpecificError()
    {
        var (model, settings, _, _, offline, status, _) = Build(source: "en", target: "ru");
        settings.OfflineOnly = true;
        // status left NotInstalled for "ru" -> pair not ready.
        offline.Handler = (_, _, _) => Task.FromException<OfflineTranslation>(new OfflineTranslationException("no model"));

        model.InputText = "hello";
        model.Translate();
        await model.CurrentTranslationTask!;

        Assert.Equal(L10n.T("error.offline.only.missing"), model.ErrorMessage);
    }

    [Fact]
    public async Task NoInternetAndOfflineNotReady_SetsNoInternetError()
    {
        var (model, _, _, online, offline, _, _) = Build(source: "en", target: "ru");
        online.Handler = (_, _, _) => Task.FromException<OnlineTranslation>(new GoogleTranslateException("network down"));
        offline.Handler = (_, _, _) => Task.FromException<OfflineTranslation>(new OfflineTranslationException("no model"));
        // status left NotInstalled -> not ready, offlineOnly is false.

        model.InputText = "hello";
        model.Translate();
        await model.CurrentTranslationTask!;

        Assert.Equal(L10n.T("error.no.internet"), model.ErrorMessage);
    }

    [Fact]
    public async Task OfflineReadyButTranslationThrows_SetsTranslateFailedError()
    {
        var (model, _, _, online, offline, status, _) = Build(source: "en", target: "ru");
        online.Handler = (_, _, _) => Task.FromException<OnlineTranslation>(new GoogleTranslateException("network down"));
        offline.Handler = (_, _, _) => Task.FromException<OfflineTranslation>(new OfflineTranslationException("inference failed"));
        status.States["ru"] = OfflineLanguageState.Installed;

        model.InputText = "hello";
        model.Translate();
        await model.CurrentTranslationTask!;

        Assert.Equal(L10n.Format("error.translate.failed", "inference failed"), model.ErrorMessage);
    }

    [Fact]
    public async Task OfflineTimeout_SetsTimeoutError()
    {
        var (model, _, _, online, offline, _, _) = Build(source: "en", target: "ru");
        online.Handler = (_, _, _) => Task.FromException<OnlineTranslation>(new GoogleTranslateException("network down"));
        offline.Handler = (_, _, _) => Task.FromException<OfflineTranslation>(new OperationCanceledException());

        model.InputText = "hello";
        model.Translate();
        await model.CurrentTranslationTask!;

        Assert.Equal(L10n.T("error.offline.timeout"), model.ErrorMessage);
    }

    [Fact]
    public async Task ResolvedPair_AutoSourceCyrillicTextWithRussianTarget_RetargetsToEnglish()
    {
        var (model, _, _, online, _, _, _) = Build(source: "auto", target: "ru");
        online.Handler = (_, _, _) => Task.FromResult(new OnlineTranslation("hello", null));

        model.InputText = "Привет"; // "Привет"
        model.Translate();
        await model.CurrentTranslationTask!;

        Assert.Single(online.Calls);
        Assert.Equal("en", online.Calls[0].Target);
    }

    [Fact]
    public async Task ResolvedPair_AutoSourceNonCyrillicTextWithRussianTarget_KeepsRussianTarget()
    {
        var (model, _, _, online, _, _, _) = Build(source: "auto", target: "ru");
        online.Handler = (_, _, _) => Task.FromResult(new OnlineTranslation("hi", null));

        model.InputText = "hello";
        model.Translate();
        await model.CurrentTranslationTask!;

        Assert.Single(online.Calls);
        Assert.Equal("ru", online.Calls[0].Target);
    }

    [Fact]
    public async Task SmartPair_DetectedSourceEqualsTarget_RetriesWithDifferentTarget()
    {
        // "de" target avoids the ru/Cyrillic special-case in ResolvedPair so the resolved target stays "de".
        var (model, _, _, online, _, _, _) = Build(source: "auto", target: "de");
        var call = 0;
        online.Handler = (_, _, _) =>
        {
            call++;
            return call == 1
                ? Task.FromResult(new OnlineTranslation("hallo ohne Übersetzung", "de"))
                : Task.FromResult(new OnlineTranslation("translated", "de"));
        };

        model.InputText = "hallo";
        model.Translate();
        await model.CurrentTranslationTask!;

        Assert.Equal(2, online.Calls.Count);
        Assert.Equal("de", online.Calls[0].Target);
        Assert.Equal("en", online.Calls[1].Target); // target != "en" -> retarget to English
        Assert.Equal("translated", model.OutputText);
    }

    [Fact]
    public async Task SmartPair_TargetAlreadyEnglish_RetargetsToUiLanguage()
    {
        L10n.Selected = AppUILanguage.Ru;
        var (model, _, _, online, _, _, _) = Build(source: "auto", target: "en");
        var call = 0;
        online.Handler = (_, _, _) =>
        {
            call++;
            return call == 1
                ? Task.FromResult(new OnlineTranslation("same text", "en"))
                : Task.FromResult(new OnlineTranslation("перевод", "en"));
        };

        model.InputText = "some english text";
        model.Translate();
        await model.CurrentTranslationTask!;

        Assert.Equal(2, online.Calls.Count);
        Assert.Equal("ru", online.Calls[1].Target); // UI language is Russian
    }

    [Fact]
    public async Task SwapLanguages_ExplicitSourceAndTarget_Swaps()
    {
        var (model, _, _, online, _, _, _) = Build(source: "en", target: "ru");
        online.Handler = (_, _, _) => Task.FromResult(new OnlineTranslation("x", null));

        model.SwapLanguages();

        Assert.Equal("ru", model.SourceCode);
        Assert.Equal("en", model.TargetCode);
    }

    [Fact]
    public void SwapLanguages_AutoSourceNoDetection_UsesDefaultCounterpart()
    {
        var (model, _, _, _, _, _, _) = Build(source: "auto", target: "ru");

        model.SwapLanguages();

        Assert.Equal("ru", model.SourceCode);
        Assert.Equal("en", model.TargetCode);
    }

    [Fact]
    public async Task SwapLanguages_AutoSourceWithDetection_UsesDetectedLanguage()
    {
        var (model, _, _, online, _, _, _) = Build(source: "auto", target: "de");
        online.Handler = (_, _, _) => Task.FromResult(new OnlineTranslation("bonjour->x", "fr"));

        model.InputText = "some text";
        model.Translate();
        await model.CurrentTranslationTask!;

        model.SwapLanguages();

        Assert.Equal("de", model.SourceCode);
        Assert.Equal("fr", model.TargetCode);
    }

    [Fact]
    public void SwapLanguages_CollisionResolved_PicksDefaultCounterpart()
    {
        var (model, _, _, _, _, _, _) = Build(source: "ru", target: "ru");

        model.SwapLanguages();

        Assert.Equal("ru", model.SourceCode);
        Assert.Equal("en", model.TargetCode);
    }

    [Fact]
    public void Clear_ResetsInputOutputAndEngine()
    {
        var (model, _, _, _, _, _, _) = Build();
        model.InputText = "something";

        model.Clear();

        Assert.Equal("", model.InputText);
        Assert.Equal("", model.OutputText);
        Assert.Null(model.ErrorMessage);
        Assert.Null(model.Notice);
        Assert.Equal(EngineKind.None, model.Engine);
    }

    [Fact]
    public async Task ExplicitTranslateAfterSettingInput_WithAutoTranslate_TranslatesOnce()
    {
        var (model, settings, history, online, _, _, _) = Build(source: "en", target: "ru");
        settings.AutoTranslate = true;
        online.Handler = (_, _, _) => Task.FromResult(new OnlineTranslation("привет", null));

        model.InputText = "hello";
        model.Translate();
        await model.CurrentTranslationTask!;
        await Task.Delay(1000); // past the 700 ms auto-translate debounce

        Assert.Single(online.Calls);
        Assert.Single(history.Entries);
    }

    [Fact]
    public async Task AutoTranslate_StillTranslatesAfterTyping()
    {
        var (model, settings, _, online, _, _, _) = Build(source: "en", target: "ru");
        settings.AutoTranslate = true;
        online.Handler = (_, _, _) => Task.FromResult(new OnlineTranslation("привет", null));

        model.InputText = "hello";
        await Task.Delay(1000);
        if (model.CurrentTranslationTask is { } task)
        {
            await task;
        }

        Assert.Single(online.Calls);
        Assert.Equal("привет", model.OutputText);
    }

    [Fact]
    public void CharCounterAndLimitFlags()
    {
        var (model, _, _, _, _, _, _) = Build();
        model.InputText = new string('a', 10);
        Assert.Equal($"10 / {TranslatorModel.MaxInputChars}", model.CharCounterText);
        Assert.False(model.IsNearLimit);
        Assert.False(model.IsOverLimit);

        model.InputText = new string('a', (int)(TranslatorModel.MaxInputChars * 0.95));
        Assert.True(model.IsNearLimit);
        Assert.False(model.IsOverLimit);

        model.InputText = new string('a', TranslatorModel.MaxInputChars + 5);
        Assert.True(model.IsOverLimit);
    }
}
