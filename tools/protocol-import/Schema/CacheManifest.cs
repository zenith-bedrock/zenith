using System.Security.Cryptography;
using System.Text.Json;

namespace Zenith.ProtocolImport.Schema;

/// <summary>Provenance for one complete, immutable schema snapshot.</summary>
internal sealed record CacheManifest(
    string Source,
    string RequestedRef,
    string ResolvedSha,
    DateTimeOffset PulledAtUtc,
    IReadOnlyList<CacheFile> Files);

internal sealed record CacheFile(string Path, string Sha256);

/// <summary>
/// Publishes complete source snapshots before switching the small active-manifest pointer.
/// Readers therefore see either the previous verified snapshot or the new verified snapshot,
/// never a directory half-populated by a failed pull.
/// </summary>
internal static class SchemaCache
{
    private const string CurrentManifestFile = "current.json";
    private const string SnapshotsDirectory = "snapshots";

    public static async Task<CacheManifest> PublishAsync(
        string cacheDir, string source, string requestedRef, string resolvedSha,
        Func<string, CancellationToken, Task> download, CancellationToken ct)
    {
        var sourceRoot = Path.Combine(cacheDir, source);
        var snapshotsRoot = Path.Combine(sourceRoot, SnapshotsDirectory);
        Directory.CreateDirectory(snapshotsRoot);

        var staging = Path.Combine(snapshotsRoot, ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            await download(staging, ct);
            var files = Directory.GetFiles(staging, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new CacheFile(
                    Path.GetRelativePath(staging, path).Replace(Path.DirectorySeparatorChar, '/'),
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()))
                .ToList();

            var manifest = new CacheManifest(source, requestedRef, resolvedSha, DateTimeOffset.UtcNow, files);
            await WriteJsonAsync(Path.Combine(staging, "manifest.json"), manifest, ct);

            var snapshot = Path.Combine(snapshotsRoot, resolvedSha);
            if (!Directory.Exists(snapshot))
                Directory.Move(staging, snapshot);
            else
                Directory.Delete(staging, recursive: true);

            // File replacement is the only mutable operation. It points exclusively at a
            // complete snapshot, so a cancelled pull cannot corrupt the active cache.
            await WriteJsonAsync(Path.Combine(sourceRoot, CurrentManifestFile + ".tmp"), manifest, ct);
            File.Move(Path.Combine(sourceRoot, CurrentManifestFile + ".tmp"),
                Path.Combine(sourceRoot, CurrentManifestFile), overwrite: true);
            return manifest;
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    public static string GetReadRoot(string cacheDir, string source)
    {
        if (File.Exists(Path.Combine(cacheDir, "manifest.json"))) return cacheDir;
        var sourceRoot = Path.Combine(cacheDir, source);
        var manifest = ReadCurrentManifest(cacheDir, source);
        if (manifest is not null)
            return Path.Combine(sourceRoot, SnapshotsDirectory, manifest.ResolvedSha);

        // Compatibility for pre-manifest caches. Report exposes this as unverified; a new pull
        // upgrades it. Keeping reads working avoids a surprising break for an offline user.
        return sourceRoot;
    }

    public static string? FindSnapshot(string cacheDir, string source, string reference)
    {
        var snapshots = Path.Combine(cacheDir, source, SnapshotsDirectory);
        if (!Directory.Exists(snapshots)) return null;
        foreach (var directory in Directory.GetDirectories(snapshots))
        {
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath)) continue;
            try
            {
                var manifest = JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(manifestPath));
                if (manifest is not null && (manifest.RequestedRef == reference || manifest.ResolvedSha == reference))
                    return directory;
            }
            catch (JsonException) { }
        }
        return null;
    }

    public static CacheManifest? ReadCurrentManifest(string cacheDir, string source)
    {
        var path = Path.Combine(cacheDir, source, CurrentManifestFile);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(path)); }
        catch (JsonException) { return null; }
    }

    public static IReadOnlyList<string> Validate(string cacheDir, string source)
    {
        var manifest = ReadCurrentManifest(cacheDir, source);
        if (manifest is null) return ["No valid current manifest; cache is legacy or inconsistent."];
        var root = Path.Combine(cacheDir, source, SnapshotsDirectory, manifest.ResolvedSha);
        var errors = new List<string>();
        foreach (var file in manifest.Files)
        {
            var path = Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) { errors.Add($"Missing cached file: {file.Path}"); continue; }
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            if (!StringComparer.Ordinal.Equals(hash, file.Sha256)) errors.Add($"Hash mismatch: {file.Path}");
        }
        return errors;
    }

    public static IReadOnlyList<string> ValidateSnapshot(string snapshotRoot)
    {
        var manifestPath = Path.Combine(snapshotRoot, "manifest.json");
        if (!File.Exists(manifestPath)) return ["Snapshot manifest is missing."];
        try
        {
            var manifest = JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(manifestPath));
            if (manifest is null) return ["Snapshot manifest is invalid."];
            var errors = new List<string>();
            foreach (var file in manifest.Files)
            {
                var path = Path.Combine(snapshotRoot, file.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) { errors.Add($"Missing cached file: {file.Path}"); continue; }
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
                if (!StringComparer.Ordinal.Equals(hash, file.Sha256)) errors.Add($"Hash mismatch: {file.Path}");
            }
            return errors;
        }
        catch (JsonException) { return ["Snapshot manifest is invalid."]; }
    }

    private static Task WriteJsonAsync(string path, object value, CancellationToken ct) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), ct);
}
