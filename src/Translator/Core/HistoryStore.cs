using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Translator.Core;

public enum EngineKind
{
    None,
    Google,
    Offline,
}

/// <summary>
/// One stored translation. Only codes are persisted (never display text derived from L10n),
/// so history stays correct when the UI language changes.
/// </summary>
public sealed class HistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Date { get; set; } = DateTimeOffset.Now;

    /// <summary>null means "auto".</summary>
    public string? SourceCode { get; set; }

    /// <summary>Language Google (or the offline engine) detected, when <see cref="SourceCode"/> is null.</summary>
    public string? DetectedSourceCode { get; set; }

    public string TargetCode { get; set; } = "";
    public EngineKind Engine { get; set; }
    public string Input { get; set; } = "";
    public string Output { get; set; } = "";

    /// <summary>e.g. "Auto (Russian) → English".</summary>
    [JsonIgnore]
    public string PairText
    {
        get
        {
            var sourceText = SourceCode is not null
                ? L10n.LanguageName(SourceCode)
                : DetectedSourceCode is not null
                    ? L10n.Format("auto.detected", L10n.LanguageName(DetectedSourceCode))
                    : L10n.T("auto");
            return $"{sourceText} → {L10n.LanguageName(TargetCode)}";
        }
    }

    [JsonIgnore]
    public string EngineText => Engine switch
    {
        EngineKind.Google => L10n.T("engine.google.label"),
        EngineKind.Offline => L10n.T("engine.offline.label"),
        _ => string.Empty,
    };

    [JsonIgnore]
    public string DateText => Date.ToLocalTime().ToString("g", L10n.CultureFor(L10n.CurrentCode));
}

/// <summary>Persists the last <see cref="Limit"/> translations to disk. Tolerant of a missing/corrupt file.</summary>
public sealed class HistoryStore
{
    public const int Limit = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public HistoryStore(string path)
    {
        _path = path;
        Entries = new ObservableCollection<HistoryEntry>(Load(path));
    }

    public static HistoryStore Load() => new(AppPaths.HistoryFile);

    public ObservableCollection<HistoryEntry> Entries { get; }

    public void Add(HistoryEntry entry)
    {
        Entries.Insert(0, entry);
        while (Entries.Count > Limit)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }
        Save();
    }

    public void Clear()
    {
        Entries.Clear();
        Save();
    }

    private static List<HistoryEntry> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }
            var json = File.ReadAllText(path);
            var entries = JsonSerializer.Deserialize<List<HistoryEntry>>(json, JsonOptions);
            return entries is { Count: > 0 } ? entries.Take(Limit).ToList() : [];
        }
        catch (Exception ex)
        {
            DebugLog.Write($"HistoryStore: load failed ({ex.GetType().Name})");
            return [];
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Entries.ToList(), JsonOptions);
            var tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"HistoryStore: save failed ({ex.GetType().Name})");
        }
    }
}
