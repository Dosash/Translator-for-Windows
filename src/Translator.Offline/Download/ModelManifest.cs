using System.Text.Json;
using System.Text.Json.Serialization;

namespace Translator.Offline;

internal sealed record ManifestFile(string Repo, string Path, long Size, string Oid, string? Sha256)
{
    [JsonIgnore]
    public string ContentId => Sha256 ?? Oid;
}

/// <summary>manifest.json stored next to a downloaded model: where it came from and what each file should be.</summary>
internal sealed class ModelManifest
{
    public const string FileName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public int FormatVersion { get; set; } = 1;
    public string ModelId { get; set; } = "";
    public string Repo { get; set; } = "";
    public string Commit { get; set; } = "";
    public string? TokenizerRepo { get; set; }
    public string? TokenizerCommit { get; set; }
    public DateTimeOffset DownloadedAt { get; set; }
    public List<ManifestFile> Files { get; set; } = [];
    public bool UpdateAvailable { get; set; }
    public DateTimeOffset? LastUpdateCheck { get; set; }

    [JsonIgnore]
    public long TotalSize => Files.Sum(f => f.Size);

    public static ModelManifest Create(OfflineModelInfo model, IReadOnlyDictionary<string, RepoSnapshot> snapshots, DateTimeOffset now)
    {
        var manifest = new ModelManifest
        {
            ModelId = model.Id,
            Repo = model.Repo,
            Commit = snapshots[model.Repo].Commit,
            TokenizerRepo = model.TokenizerRepo,
            TokenizerCommit = model.TokenizerRepo is null ? null : snapshots[model.TokenizerRepo].Commit,
            DownloadedAt = now,
        };

        foreach (var file in model.Files)
        {
            var remote = snapshots[file.Repo].Files[file.Path];
            manifest.Files.Add(new ManifestFile(file.Repo, file.Path, remote.Size, remote.GitOid, remote.Sha256));
        }

        return manifest;
    }

    public static ModelManifest? TryLoad(string modelDirectory)
    {
        try
        {
            var path = Path.Combine(modelDirectory, FileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<ModelManifest>(File.ReadAllBytes(path), JsonOptions) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Delete(string modelDirectory)
    {
        var path = Path.Combine(modelDirectory, FileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void Save(string modelDirectory)
    {
        Directory.CreateDirectory(modelDirectory);
        var path = Path.Combine(modelDirectory, FileName);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    public ManifestFile? FindFile(OfflineModelFile file) =>
        Files.FirstOrDefault(f => f.Repo == file.Repo && f.Path == file.Path);

    /// <summary>Every file the catalog needs is recorded and present on disk with the recorded size.</summary>
    public bool IsComplete(OfflineModelInfo model, string modelDirectory) =>
        model.Files.All(file =>
            FindFile(file) is { } entry
            && new FileInfo(ModelInstaller.GetLocalPath(modelDirectory, file.Path)) is { Exists: true } info
            && info.Length == entry.Size);

    /// <summary>Catalog files whose remote content differs from what was downloaded (files missing remotely are ignored).</summary>
    public IReadOnlyList<string> GetChangedFiles(OfflineModelInfo model, IReadOnlyDictionary<string, RepoSnapshot> snapshots)
    {
        var changed = new List<string>();
        foreach (var file in model.Files)
        {
            if (!snapshots.TryGetValue(file.Repo, out var snapshot) || !snapshot.Files.TryGetValue(file.Path, out var remote))
            {
                continue;
            }

            var local = FindFile(file);
            if (local is null || local.Size != remote.Size || !string.Equals(local.ContentId, remote.ContentId, StringComparison.OrdinalIgnoreCase))
            {
                changed.Add(file.Path);
            }
        }

        return changed;
    }
}
