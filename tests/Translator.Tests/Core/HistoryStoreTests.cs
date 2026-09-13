using System.IO;
using Translator.Core;

namespace Translator.Tests.Core;

public class HistoryStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public HistoryStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "TranslatorTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "history.json");
        L10n.Selected = AppUILanguage.En;
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort cleanup */ }
    }

    [Fact]
    public void AddKeepsOnlyTheLastTenNewestFirst()
    {
        var store = new HistoryStore(_path);
        for (var i = 0; i < 15; i++)
        {
            store.Add(new HistoryEntry { Input = $"in{i}", Output = $"out{i}", TargetCode = "en" });
        }

        Assert.Equal(HistoryStore.Limit, store.Entries.Count);
        Assert.Equal("in14", store.Entries[0].Input);
        Assert.Equal("in5", store.Entries[^1].Input);
    }

    [Fact]
    public void PersistsAndReloadsAcrossInstances()
    {
        var store = new HistoryStore(_path);
        store.Add(new HistoryEntry
        {
            SourceCode = "en",
            TargetCode = "ru",
            Engine = EngineKind.Google,
            Input = "hello",
            Output = "привет",
        });

        var reloaded = new HistoryStore(_path);
        Assert.Single(reloaded.Entries);
        Assert.Equal("hello", reloaded.Entries[0].Input);
        Assert.Equal("привет", reloaded.Entries[0].Output);
        Assert.Equal(EngineKind.Google, reloaded.Entries[0].Engine);
        Assert.Equal("ru", reloaded.Entries[0].TargetCode);
    }

    [Fact]
    public void ClearEmptiesAndPersists()
    {
        var store = new HistoryStore(_path);
        store.Add(new HistoryEntry { Input = "x", Output = "y", TargetCode = "en" });
        store.Clear();

        Assert.Empty(store.Entries);
        var reloaded = new HistoryStore(_path);
        Assert.Empty(reloaded.Entries);
    }

    [Fact]
    public void MissingFileLoadsEmpty()
    {
        var store = new HistoryStore(_path);
        Assert.Empty(store.Entries);
    }

    [Fact]
    public void CorruptFileLoadsEmptyWithoutThrowing()
    {
        File.WriteAllText(_path, "{ not valid json ][");
        var store = new HistoryStore(_path);
        Assert.Empty(store.Entries);
    }

    [Fact]
    public void PairTextUsesAutoDetectedWhenSourceIsNull()
    {
        L10n.Selected = AppUILanguage.En;
        var entry = new HistoryEntry { SourceCode = null, DetectedSourceCode = "ru", TargetCode = "en" };
        Assert.Equal("Auto (Russian) → English", entry.PairText);
    }

    [Fact]
    public void PairTextUsesExplicitSourceWhenPresent()
    {
        L10n.Selected = AppUILanguage.En;
        var entry = new HistoryEntry { SourceCode = "de", TargetCode = "fr" };
        Assert.Equal("German → French", entry.PairText);
    }

    [Fact]
    public void EngineTextReflectsEngineKind()
    {
        L10n.Selected = AppUILanguage.En;
        Assert.Equal("Google · online", new HistoryEntry { Engine = EngineKind.Google, TargetCode = "en" }.EngineText);
        Assert.Equal("Offline · on this PC", new HistoryEntry { Engine = EngineKind.Offline, TargetCode = "en" }.EngineText);
        Assert.Equal("", new HistoryEntry { Engine = EngineKind.None, TargetCode = "en" }.EngineText);
    }
}
