using System.Text.Json;
using Zenith.World;

namespace Zenith.Gameplay.Inventory;

/// <summary>
/// Max durability per item, from the embedded <c>item_durability.json</c> (built by <c>bedrock-data durability</c>
/// from an open-source extraction, see that file's <c>source</c>). Resolved against this build's item palette, so an
/// unknown name or a non-positive value fails the load instead of leaving an inert entry. An item that is not in the
/// table has no durability (it never wears), which is the truth for everything the source does not list.
/// </summary>
sealed class ItemDurability
{
    public const string EmbeddedLogicalName = "item_durability.json";

    private readonly Dictionary<int, int> _maxByItemId;

    private ItemDurability(Dictionary<int, int> maxByItemId) => _maxByItemId = maxByItemId;

    public static ItemDurability CreateDefault(ItemPalette palette)
    {
        using var stream = typeof(ItemDurability).Assembly.GetManifestResourceStream(EmbeddedLogicalName)
            ?? throw new InvalidOperationException($"Embedded resource '{EmbeddedLogicalName}' not found.");
        return FromStream(stream, palette);
    }

    public static ItemDurability FromStream(Stream json, ItemPalette palette)
    {
        using var doc = JsonDocument.Parse(json);
        var map = new Dictionary<int, int>();
        foreach (var entry in doc.RootElement.GetProperty("items").EnumerateObject())
        {
            if (!palette.TryGet(entry.Name, out var id))
                throw new InvalidDataException($"Durability data: unknown item '{entry.Name}'.");
            var max = entry.Value.GetInt32();
            if (max <= 0)
                throw new InvalidDataException($"Durability data: '{entry.Name}' has max durability {max}.");
            if (!map.TryAdd(id, max))
                throw new InvalidDataException($"Durability data: duplicate item '{entry.Name}'.");
        }

        return new ItemDurability(map);
    }

    public int Count => _maxByItemId.Count;

    /// <summary>The max durability of the item stack <paramref name="id"/>; false for blocks and items that do not wear.</summary>
    public bool TryGetMax(StackId id, out int max)
    {
        max = 0;
        return !id.IsBlock && _maxByItemId.TryGetValue(id.Value, out max);
    }
}
