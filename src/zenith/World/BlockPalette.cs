using System.IO.Compression;
using System.Reflection;
using System.Text;
using Zenith.Nbt;

namespace Zenith.World;

/// <summary>
/// Mapa name → network_id a partir de block_palette.nbt.
/// Índice secundário: name + uma string-state (ex. baú <c>cardinal_direction</c>) — não é um resolutor genérico de block-state.
/// </summary>
sealed class BlockPalette
{
    private readonly Dictionary<string, int> _byName;
    private readonly Dictionary<string, int> _byNameAndState;

    public BlockPalette(Dictionary<string, int> byName, Dictionary<string, int> byNameAndState)
    {
        _byName = byName;
        _byNameAndState = byNameAndState;
    }

    public int Count => _byName.Count;

    public bool TryGet(string name, out int networkId) => _byName.TryGetValue(name, out networkId);

    public int Require(string name) =>
        TryGet(name, out var id)
            ? id
            : throw new InvalidOperationException($"Block palette missing '{name}'.");

    public bool TryGet(string name, string stateKey, string stateValue, out int networkId) =>
        _byNameAndState.TryGetValue(StateKey(name, stateKey, stateValue), out networkId);

    public int Require(string name, string stateKey, string stateValue) =>
        TryGet(name, stateKey, stateValue, out var id)
            ? id
            : throw new InvalidOperationException(
                $"Block palette missing '{name}' with {stateKey}={stateValue}.");

    internal static string StateKey(string name, string stateKey, string stateValue)
    {
        var sb = new StringBuilder(name.Length + stateKey.Length + stateValue.Length + 2);
        sb.Append(name);
        sb.Append('\0');
        sb.Append(stateKey);
        sb.Append('\0');
        sb.Append(stateValue);
        return sb.ToString();
    }
}

static class BlockPaletteLoader
{
    public const string EmbeddedResourceName = "block_palette.nbt";

    public static BlockPalette FromEmbeddedResource(Assembly? assembly = null)
    {
        assembly ??= typeof(BlockPaletteLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{EmbeddedResourceName}' not found.");
        return FromGzipStream(stream);
    }

    public static BlockPalette FromGzipFile(string path)
    {
        using var fs = File.OpenRead(path);
        return FromGzipStream(fs);
    }

    public static BlockPalette FromGzipStream(Stream gzipStream)
    {
        Span<byte> magic = stackalloc byte[2];
        var read = gzipStream.Read(magic);
        if (read < 2 || magic[0] != 0x1f || magic[1] != 0x8b)
            throw new InvalidOperationException("block_palette.nbt is not gzip (expected magic 1f 8b).");

        // Re-open: some streams aren't seekable after peek — use MemoryStream of remainder.
        using var prefixed = new MemoryStream();
        prefixed.Write(magic);
        gzipStream.CopyTo(prefixed);
        prefixed.Position = 0;

        using var gz = new GZipStream(prefixed, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        gz.CopyTo(raw);
        return FromNbtBytes(raw.ToArray());
    }

    /// <summary>Palette dumps in this repo are Java-style big-endian NBT (after gunzip).</summary>
    public static BlockPalette FromNbtBytes(ReadOnlySpan<byte> nbtBytes) =>
        FromBytes(nbtBytes, NbtEncoding.BigEndian);

    public static BlockPalette FromBytes(ReadOnlySpan<byte> nbtBytes, NbtEncoding encoding)
    {
        var decoded = NbtCodec.Decode(nbtBytes, encoding);
        if (!decoded.Root.Tag.TryGetCompound(out var root))
            throw new InvalidOperationException("block_palette root is not a compound.");

        if (!root.TryGet("blocks", out var blocksTag) || !blocksTag.TryGetList(out var blocks))
            throw new InvalidOperationException("block_palette missing list 'blocks'.");

        // Prefer empty-states entry per name; first entry fills until a preferred one appears.
        var preferred = new Dictionary<string, int>(StringComparer.Ordinal);
        var fallback = new Dictionary<string, int>(StringComparer.Ordinal);
        var byState = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < blocks.Count; i++)
        {
            if (!blocks[i].TryGetCompound(out var entry))
                continue;
            if (!entry.TryGet("name", out var nameTag) || !nameTag.TryGetString(out var name))
                continue;
            if (!entry.TryGet("network_id", out var idTag) || !idTag.TryGetInt(out var networkId))
                continue;

            fallback.TryAdd(name, networkId);

            if (!entry.TryGet("states", out var statesTag) || !statesTag.TryGetCompound(out var states))
                continue;

            if (states.Count == 0)
            {
                preferred[name] = networkId;
                continue;
            }

            // Single string-state lookup only (chest cardinal_direction). Multi-property blobs ignored here.
            if (states.Count != 1)
                continue;
            var (stateKey, stateTag) = states.Entries[0];
            if (!stateTag.TryGetString(out var stateValue))
                continue;
            byState[BlockPalette.StateKey(name, stateKey, stateValue)] = networkId;
        }

        foreach (var (name, id) in preferred)
            fallback[name] = id;

        return new BlockPalette(fallback, byState);
    }
}
