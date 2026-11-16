using System.Text.Json;
using Zenith.Gameplay.Policy;

namespace Zenith.Modules.Protect;

/// <summary>
/// The saved form of the regions: a small versioned JSON document. Reading is strict: bytes that cannot be understood throw, because
/// protection that silently forgets its regions is worse than a server that refuses to start and says why.
/// </summary>
static class ProtectRegionCodec
{
    private const int CurrentVersion = 2;

    /// <summary>Version 1 had no allowed causes (every region strict); it is still read.</summary>
    private const int FirstVersion = 1;

    private static readonly JsonSerializerOptions Options = new();

    public static byte[] Encode(IReadOnlyList<ProtectRegion> regions) => JsonSerializer.SerializeToUtf8Bytes(new Document
    {
        Version = CurrentVersion,
        Regions = [.. regions.Select(r => new Entry { Name = r.Name, Min = [r.MinX, r.MinY, r.MinZ], Max = [r.MaxX, r.MaxY, r.MaxZ], Allow = AllowedNames(r) })]
    }, Options);

    /// <exception cref="InvalidDataException">The bytes are not a region list this build understands.</exception>
    public static IReadOnlyList<ProtectRegion> Decode(byte[] bytes)
    {
        try
        {
            var document = JsonSerializer.Deserialize<Document>(bytes, Options) ?? throw new InvalidDataException("empty document");
            if (document.Version is < FirstVersion or > CurrentVersion) throw new InvalidDataException($"unsupported version {document.Version}");

            // Loaded through the store's own rules, so duplicate or malformed names are refused the same way as from the command.
            var accepted = new ProtectRegions();
            foreach (var entry in document.Regions ?? [])
            {
                if (entry.Min is not { Length: 3 } min || entry.Max is not { Length: 3 } max || entry.Name is null)
                    throw new InvalidDataException("a region needs a name, min[3] and max[3]");
                var region = ProtectRegion.Between(entry.Name, min[0], min[1], min[2], max[0], max[1], max[2]);
                foreach (var cause in entry.Allow ?? [])
                {
                    // Unknown names are refused, not skipped: a region that silently lost a rule is a region nobody can reason about.
                    if (!Enum.TryParse<EnvironmentCause>(cause, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed))
                        throw new InvalidDataException($"region '{entry.Name}': unknown cause '{cause}'");
                    region = region.WithCause(parsed, allowed: true);
                }

                if (!accepted.TryAdd(region, out var error))
                    throw new InvalidDataException($"region '{entry.Name}': {error}");
            }

            return [.. accepted.Snapshot];
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
    }

    private static string[]? AllowedNames(ProtectRegion region)
    {
        var names = Enum.GetValues<EnvironmentCause>().Where(region.Allows).Select(c => c.ToString()).ToArray();
        return names.Length == 0 ? null : names;
    }

    private sealed class Document
    {
        public int Version { get; set; }
        public List<Entry>? Regions { get; set; }
    }

    private sealed class Entry
    {
        public string? Name { get; set; }
        public int[]? Min { get; set; }
        public int[]? Max { get; set; }
        public string[]? Allow { get; set; }
    }
}
