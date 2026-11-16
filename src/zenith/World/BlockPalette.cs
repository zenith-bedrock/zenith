using System.IO.Compression;
using System.Reflection;
using System.Text;
using Zenith.Nbt;

namespace Zenith.World;

/// <summary>
/// Mapa name → network_id a partir de block_palette.nbt.
/// Índice secundário: name + uma string-state (ex. baú <c>cardinal_direction</c>) — não é um resolutor genérico de block-state.
/// Reverse <c>network_id → name</c> covers every dump entry (incl. multi-state skipped by the secondary index).
/// </summary>
sealed class BlockPalette
{
    private readonly Dictionary<string, int> _byName;
    private readonly Dictionary<string, int> _byNameAndState;
    private readonly Dictionary<int, string> _nameByRuntime;

    public BlockPalette(
        Dictionary<string, int> byName,
        Dictionary<string, int> byNameAndState,
        Dictionary<int, string> nameByRuntime)
    {
        _byName = byName;
        _byNameAndState = byNameAndState;
        _nameByRuntime = nameByRuntime;
    }

    private readonly Dictionary<int, BlockState> _stateByRuntime = new();
    private readonly Dictionary<string, int> _byFullState = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, List<string>>> _propertyValues = new(StringComparer.Ordinal);

    /// <summary>Every state with full property data, for building typed lookup tables.</summary>
    internal IEnumerable<KeyValuePair<int, BlockState>> AllStates => _stateByRuntime;

    /// <summary>Total number of distinct block states (runtime ids) with full property data.</summary>
    public int StateCount => _stateByRuntime.Count;

    /// <summary>All properties of a runtime id (empty for stateless blocks); false when the id is unknown.</summary>
    public bool TryGetState(int networkId, out BlockState state) => _stateByRuntime.TryGetValue(networkId, out state!);

    /// <summary>Exact lookup of the runtime id for a name plus its complete property set.</summary>
    public bool TryGetExact(string name, BlockState state, out int networkId) =>
        _byFullState.TryGetValue(state.CanonicalKey(name), out networkId);

    /// <summary>
    /// Runtime id for the name with <paramref name="overrides"/> applied on top of the block's default state.
    /// Overrides naming a property or value the block does not have make the lookup fail; it never guesses.
    /// </summary>
    public bool TryResolve(string name, IReadOnlyDictionary<string, string> overrides, out int networkId)
    {
        networkId = 0;
        if (!_byName.TryGetValue(name, out var defaultId) || !_stateByRuntime.TryGetValue(defaultId, out var baseState))
            return false;
        var merged = baseState.With(overrides);
        return merged is not null && _byFullState.TryGetValue(merged.CanonicalKey(name), out networkId);
    }

    /// <summary>Property names and their ordered legal values for a block name (null for stateless blocks).</summary>
    public IReadOnlyDictionary<string, List<string>>? PropertiesOf(string name) =>
        _propertyValues.TryGetValue(name, out var values) ? values : null;

    internal void AddState(string name, int networkId, BlockState state)
    {
        _stateByRuntime.TryAdd(networkId, state);
        _byFullState.TryAdd(state.CanonicalKey(name), networkId);
        if (state.Count == 0) return;
        if (!_propertyValues.TryGetValue(name, out var props))
            _propertyValues[name] = props = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (key, value) in state.Entries)
        {
            if (!props.TryGetValue(key, out var list)) props[key] = list = new List<string>();
            if (!list.Contains(value)) list.Add(value);
        }
    }

    public int Count => _byName.Count;

    /// <summary>Dump entries with a recorded network_id (includes multi-state variants).</summary>
    public int ReverseCount => _nameByRuntime.Count;

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

    /// <summary>First-wins reverse map from NBT parse (ADR §12 adendo).</summary>
    public bool TryGetName(int networkId, out string name) =>
        _nameByRuntime.TryGetValue(networkId, out name!);

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
        var nameByRuntime = new Dictionary<int, string>(blocks.Count);
        var fullStates = new List<(string Name, int Id, BlockState State)>(blocks.Count);

        for (var i = 0; i < blocks.Count; i++)
        {
            if (!blocks[i].TryGetCompound(out var entry))
                continue;
            if (!entry.TryGet("name", out var nameTag) || !nameTag.TryGetString(out var name))
                continue;
            if (!entry.TryGet("network_id", out var idTag) || !idTag.TryGetInt(out var networkId))
                continue;

            // Every dump entry — first-wins if ids collide (should not).
            nameByRuntime.TryAdd(networkId, name);

            fallback.TryAdd(name, networkId);

            if (!entry.TryGet("states", out var statesTag) || !statesTag.TryGetCompound(out var states))
                continue;

            fullStates.Add((name, networkId, BlockState.FromNbt(states)));

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

        var palette = new BlockPalette(fallback, byState, nameByRuntime);
        foreach (var (name, id, state) in fullStates) palette.AddState(name, id, state);
        return palette;
    }
}
