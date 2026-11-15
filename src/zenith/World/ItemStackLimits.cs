using System.Collections.Frozen;
using System.Text.Json;

namespace Zenith.World;

/// <summary>Item capacity, independent of where a stack is held (ADR §160).</summary>
static class ItemStackLimits
{
    public const int Default = 64;
    private static readonly FrozenDictionary<string, int> ByName = LoadNames();
    private static readonly Lock LoadGate = new();
    private static volatile FrozenDictionary<int, int>? _items;

    public static void Load(ItemPalette palette)
    {
        var table = Build(palette);
        lock (LoadGate) _items = table;
    }

    private static FrozenDictionary<int, int> Build(ItemPalette palette) => palette.Entries
        .Where(e => ByName.ContainsKey(e.Name))
        .ToFrozenDictionary(e => (int)e.NetworkId, e => ByName[e.Name]);

    public static int For(StackId id)
    {
        if (id.IsBlock)
            return Blocks.TryGetName(id.Value, out var name) ? ByName.GetValueOrDefault(name, Default) : Default;
        var table = _items;
        if (table is null)
            lock (LoadGate) table = _items ??= Build(ItemPaletteLoader.FromEmbeddedResource());
        return table.GetValueOrDefault(id.Value, Default);
    }

    /// <summary>Old oversized stacks may drain in place, never grow or move oversized.</summary>
    public static bool CanReplace(Player.InventorySlot previous, Player.InventorySlot next) =>
        next.IsEmpty || (next.Count <= Default && (next.Count <= For(next.Id) ||
            (!previous.IsEmpty && previous.Id == next.Id && next.Count <= previous.Count)));

    private static FrozenDictionary<string, int> LoadNames()
    {
        using var stream = typeof(ItemStackLimits).Assembly.GetManifestResourceStream("item_stack_sizes.json")
            ?? throw new InvalidOperationException("Missing item stack capacities.");
        using var doc = JsonDocument.Parse(stream);
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in doc.RootElement.GetProperty("items").EnumerateObject())
        {
            var size = row.Value.GetInt32();
            if (size is < 1 or > Default) throw new InvalidOperationException($"Invalid stack size for {row.Name}.");
            names.Add(row.Name, size);
        }
        return names.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
