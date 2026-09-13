using System.IO;
using Translator.Offline;

namespace Translator.Tests.Offline;

public sealed class ModelManifestTests : IDisposable
{
    private static readonly OfflineModelInfo Model = OfflineModelCatalog.GetModel("opus-mt-en-mul");
    private readonly TemporaryDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void UnchangedRemoteHasNoChanges()
    {
        var snapshots = Snapshot();
        var manifest = ModelManifest.Create(Model, snapshots, DateTimeOffset.UtcNow);
        Assert.Empty(manifest.GetChangedFiles(Model, snapshots));
        Assert.Equal("commit-1", manifest.Commit);
        Assert.Equal(Model.Files.Count, manifest.Files.Count);
    }

    [Fact]
    public void ChangedLfsHashIsAnUpdate()
    {
        var manifest = ModelManifest.Create(Model, Snapshot(), DateTimeOffset.UtcNow);
        var changed = Snapshot(file => file.Path == Model.DecoderPath ? file with { Sha256 = "changed" } : file);
        Assert.Equal([Model.DecoderPath], manifest.GetChangedFiles(Model, changed));
    }

    [Fact]
    public void ChangedPlainFileOrSizeIsAnUpdate()
    {
        var manifest = ModelManifest.Create(Model, Snapshot(), DateTimeOffset.UtcNow);
        var changed = Snapshot(file => file.Path switch
        {
            "vocab.json" => file with { GitOid = "changed" },
            "source.spm" => file with { Size = file.Size + 1 },
            _ => file,
        });
        Assert.Equal(["source.spm", "vocab.json"], manifest.GetChangedFiles(Model, changed).Order());
    }

    [Fact]
    public void FilesMissingRemotelyAreNotUpdates()
    {
        var manifest = ModelManifest.Create(Model, Snapshot(), DateTimeOffset.UtcNow);
        var snapshot = Snapshot()[Model.Repo];
        var files = snapshot.Files.Where(p => p.Key != "generation_config.json").ToDictionary();
        Assert.Empty(manifest.GetChangedFiles(Model, new Dictionary<string, RepoSnapshot> { [Model.Repo] = snapshot with { Files = files } }));
    }

    [Fact]
    public void SavesLoadsAndChecksFilesOnDisk()
    {
        var manifest = ModelManifest.Create(Model, Snapshot(), DateTimeOffset.UtcNow);
        manifest.Save(_directory.Path);
        var loaded = ModelManifest.TryLoad(_directory.Path);
        Assert.NotNull(loaded);
        Assert.Equal(manifest.Files, loaded.Files);
        Assert.Equal(manifest.Commit, loaded.Commit);
        Assert.False(loaded.IsComplete(Model, _directory.Path));

        foreach (var entry in manifest.Files)
        {
            var path = ModelInstaller.GetLocalPath(_directory.Path, entry.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[entry.Size]);
        }

        Assert.True(loaded.IsComplete(Model, _directory.Path));
        File.WriteAllBytes(ModelInstaller.GetLocalPath(_directory.Path, Model.EncoderPath), [1, 2, 3]);
        Assert.False(loaded.IsComplete(Model, _directory.Path));
    }

    [Fact]
    public void CorruptManifestIsIgnored()
    {
        Directory.CreateDirectory(_directory.Path);
        File.WriteAllText(Path.Combine(_directory.Path, ModelManifest.FileName), "{ not json");
        Assert.Null(ModelManifest.TryLoad(_directory.Path));
    }

    private static Dictionary<string, RepoSnapshot> Snapshot(Func<RemoteFile, RemoteFile>? change = null)
    {
        var files = Model.Files.ToDictionary(
            f => f.Path,
            f => (change ?? (x => x))(new RemoteFile(f.Path, 100 + f.Path.Length, "oid-" + f.Path, f.Path.EndsWith(".onnx", StringComparison.Ordinal) ? "sha-" + f.Path : null)));
        return new Dictionary<string, RepoSnapshot> { [Model.Repo] = new(Model.Repo, "commit-1", files, DateTimeOffset.UtcNow) };
    }
}
