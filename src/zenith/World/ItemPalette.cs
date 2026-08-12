using System.Text.Json;

namespace Zenith.World;

/// <summary>Entrada de <c>item_palette.json</c>.</summary>
readonly record struct ItemPaletteEntry(string Name, short NetworkId, int Version, bool ComponentBased);

/// <summary>Mapa bidirecional da palette completa do cliente.</summary>
sealed class ItemPalette
{
    private readonly Dictionary<string, short> _byName;
    private readonly Dictionary<short, string> _byNetworkId;
    private readonly ItemPaletteEntry[] _entries;

    public ItemPalette(IReadOnlyList<ItemPaletteEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries.ToArray();
        _byName = new Dictionary<string, short>(entries.Count, StringComparer.Ordinal);
        _byNetworkId = new Dictionary<short, string>(entries.Count);
        foreach (var e in _entries)
        {
            _byName[e.Name] = e.NetworkId;
            _byNetworkId[e.NetworkId] = e.Name;
        }
    }

    public int Count => _entries.Length;
    public IReadOnlyList<ItemPaletteEntry> Entries => _entries;

    public bool TryGet(string name, out short networkId) => _byName.TryGetValue(name, out networkId);

    /// <summary>Confirms that a domain item network id is part of this client palette.</summary>
    public bool TryGetName(int networkId, out string name)
    {
        if (networkId is >= short.MinValue and <= short.MaxValue &&
            _byNetworkId.TryGetValue((short)networkId, out name!))
            return true;
        name = string.Empty;
        return false;
    }

    public short Require(string name) =>
        TryGet(name, out var id)
            ? id
            : throw new InvalidOperationException($"Item palette missing '{name}'.");
}

static class ItemPaletteLoader
{
    public const string EmbeddedLogicalName = "item_palette.json";

    public static ItemPalette FromEmbeddedResource(System.Reflection.Assembly? assembly = null)
    {
        assembly ??= typeof(ItemPaletteLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedLogicalName)
            ?? throw new InvalidOperationException($"Embedded resource '{EmbeddedLogicalName}' not found.");
        return FromJsonStream(stream);
    }

    public static ItemPalette FromJsonFile(string path)
    {
        using var fs = File.OpenRead(path);
        return FromJsonStream(fs);
    }

    public static ItemPalette FromJsonStream(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("item_palette.json missing array 'items'.");

        var list = new List<ItemPaletteEntry>(items.GetArrayLength());
        foreach (var el in items.EnumerateArray())
        {
            var name = el.GetProperty("name").GetString()
                ?? throw new InvalidOperationException("item_palette entry missing name.");
            var id = el.GetProperty("id").GetInt32();
            if (id is < short.MinValue or > short.MaxValue)
                throw new InvalidOperationException($"item_palette id out of i16 range for '{name}': {id}");
            var version = el.TryGetProperty("version", out var v) ? v.GetInt32() : 0;
            var componentBased = el.TryGetProperty("component_based", out var c) && c.GetBoolean();
            list.Add(new ItemPaletteEntry(name, (short)id, version, componentBased));
        }

        if (list.Count == 0)
            throw new InvalidOperationException("item_palette.json has zero items.");

        return new ItemPalette(list);
    }
}
