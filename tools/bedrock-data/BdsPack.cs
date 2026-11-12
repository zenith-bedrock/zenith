using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace Zenith.BedrockData;

/// <summary>
/// Reader for a stock BDS install's layered behaviour packs (<c>behavior_packs/vanilla*</c>).
/// How BDS loads its catalogs (observed): a <c>vanilla</c> baseline plus one pack per game version, applied in
/// version order; a file with the same name in a later pack REPLACES the earlier one. Files ship in
/// <c>.brarchive</c> containers: 8-byte magic, u32 count, u32 version, then <c>count</c> 256-byte records
/// (u8 name length, name, zero pad, u32 offset, u32 size; offset relative to the data area right after the table),
/// then the blobs. Sibling loose files in a pack directory are read too.
/// </summary>
internal static partial class BdsPack
{
    private static readonly byte[] Magic = [0x7d, 0x27, 0x25, 0xb1, 0xa0, 0x52, 0x70, 0x26];
    private const int Header = 16;
    private const int Record = 256;

    public static Dictionary<string, byte[]> ReadBrArchive(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < Header || !data.AsSpan(0, 8).SequenceEqual(Magic))
            throw new InvalidDataException($"{path}: not a brarchive");
        var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8)));
        var dataStart = Header + count * Record;
        var result = new Dictionary<string, byte[]>(count, StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var rec = data.AsSpan(Header + i * Record, Record);
            var name = Encoding.UTF8.GetString(rec.Slice(1, rec[0]));
            var offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(rec[^8..]));
            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(rec[^4..]));
            result[name] = data.AsSpan(dataStart + offset, size).ToArray();
        }
        return result;
    }

    /// <summary><c>vanilla</c> first, then versioned packs ascending. Chemistry/editor/experimental packs are not base data.</summary>
    public static IReadOnlyList<string> PackOrder(string bdsRoot)
    {
        var root = Path.Combine(bdsRoot, "behavior_packs");
        return Directory.GetDirectories(root).Select(Path.GetFileName).OfType<string>()
            .Where(d => d == "vanilla" || VersionedPack().IsMatch(d))
            .OrderBy(p => p == "vanilla" ? 0 : 1)
            .ThenBy(VersionKey, Comparer<int[]>.Create(CompareVersions))
            .ToList();
    }

    /// <summary>Merged <c>file name -> (pack, bytes)</c> for one catalog (<c>recipes</c>, <c>items</c>, <c>biomes</c>, ...).</summary>
    public static Dictionary<string, (string Pack, byte[] Data)> LoadCategory(string bdsRoot, string category)
    {
        var root = Path.Combine(bdsRoot, "behavior_packs");
        var merged = new Dictionary<string, (string, byte[])>(StringComparer.Ordinal);
        foreach (var pack in PackOrder(bdsRoot))
        {
            foreach (var archive in new[]
                     {
                         Path.Combine(root, pack, "__brarchive", category + ".brarchive"),
                         Path.Combine(root, pack, category + ".brarchive"),
                     })
            {
                if (!File.Exists(archive)) continue;
                foreach (var (name, blob) in ReadBrArchive(archive)) merged[name] = (pack, blob);
            }

            var loose = Path.Combine(root, pack, category);
            if (!Directory.Exists(loose)) continue;
            foreach (var file in Directory.GetFiles(loose))
                merged[Path.GetFileName(file)] = (pack, File.ReadAllBytes(file));
        }
        return merged;
    }

    private static int[] VersionKey(string pack)
    {
        var m = VersionSuffix().Match(pack);
        return m.Success ? m.Groups[1].Value.Split('.').Select(int.Parse).ToArray() : [];
    }

    private static int CompareVersions(int[] a, int[] b)
    {
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            if (a[i] != b[i]) return a[i].CompareTo(b[i]);
        return a.Length.CompareTo(b.Length);
    }

    [GeneratedRegex(@"^vanilla_\d+(\.\d+)*$")]
    private static partial Regex VersionedPack();

    [GeneratedRegex(@"_(\d+(?:\.\d+)*)$")]
    private static partial Regex VersionSuffix();
}
