namespace Translator.Offline;

/// <summary>
/// Brings one model directory to the given remote snapshot: unchanged files are kept, the rest are staged as
/// verified .partial files and swapped in together, then the manifest is rewritten.
/// </summary>
internal sealed class ModelInstaller(HuggingFaceHub hub, FileDownloader downloader)
{
    public async Task<ModelManifest> InstallAsync(
        OfflineModelInfo model,
        string modelDirectory,
        IReadOnlyDictionary<string, RepoSnapshot> snapshots,
        ModelManifest? existing,
        Action<string, long> progress,
        Action beforeReplace,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(modelDirectory);
        var staged = new List<(string Partial, string Destination)>();
        foreach (var file in model.Files)
        {
            var snapshot = snapshots[file.Repo];
            if (!snapshot.Files.TryGetValue(file.Path, out var remote))
            {
                throw new InvalidDataException($"{file.Repo} no longer contains {file.Path}.");
            }

            var destination = GetLocalPath(modelDirectory, file.Path);
            var key = file.Repo + "/" + file.Path;
            if (await IsCurrentAsync(destination, remote, existing?.FindFile(file), cancellationToken).ConfigureAwait(false))
            {
                progress(key, remote.Size);
                continue;
            }

            var uri = hub.GetDownloadUri(file.Repo, snapshot.Commit, file.Path);
            var partial = await downloader
                .DownloadToPartialAsync(uri, destination, remote, bytes => progress(key, bytes), cancellationToken)
                .ConfigureAwait(false);
            staged.Add((partial, destination));
        }

        if (staged.Count > 0)
        {
            beforeReplace();
            // Without a manifest the model counts as not installed, so a crash mid-swap is never mistaken for a valid model.
            ModelManifest.Delete(modelDirectory);
            foreach (var (partial, destination) in staged)
            {
                await MoveWithRetryAsync(partial, destination, cancellationToken).ConfigureAwait(false);
            }
        }

        var manifest = ModelManifest.Create(model, snapshots, DateTimeOffset.UtcNow);
        manifest.Save(modelDirectory);
        return manifest;
    }

    public static string GetLocalPath(string modelDirectory, string repoPath) =>
        Path.Combine(modelDirectory, repoPath.Replace('/', Path.DirectorySeparatorChar));

    private static async Task<bool> IsCurrentAsync(string destination, RemoteFile remote, ManifestFile? recorded, CancellationToken cancellationToken)
    {
        var info = new FileInfo(destination);
        if (!info.Exists || info.Length != remote.Size)
        {
            return false;
        }

        if (recorded is not null && recorded.Size == remote.Size && string.Equals(recorded.ContentId, remote.ContentId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return await FileDownloader.MatchesAsync(destination, remote, cancellationToken).ConfigureAwait(false);
    }

    private static async Task MoveWithRetryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < 5 && ex is IOException or UnauthorizedAccessException)
            {
                // A just-released ONNX session or an antivirus scan can hold the old file briefly.
                await Task.Delay(200 * attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
