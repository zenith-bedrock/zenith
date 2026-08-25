using System.Text.Json;
using Zenith.Nbt;
using Zenith.World;

namespace Zenith.Gameplay.Inventory;

/// <summary>One entry from `creative_items.json`'s `items` array (ADR §140).</summary>
readonly record struct CreativeItemData(StackId Id, int GroupIndex);

/// <summary>One entry from `creative_items.json`'s `groups` array (ADR §140).</summary>
readonly record struct CreativeGroupData(int Category, string Name, StackId Icon);

/// <summary>Full parsed contents of `creative_items.json` — the real vanilla creative catalog.</summary>
sealed class CreativeItemsData
{
    public CreativeItemsData(IReadOnlyList<CreativeGroupData> groups, IReadOnlyList<CreativeItemData> items)
    {
        Groups = groups;
        Items = items;
    }

    public IReadOnlyList<CreativeGroupData> Groups { get; }
    public IReadOnlyList<CreativeItemData> Items { get; }
}

/// <summary>
/// Loads the real vanilla creative catalog (ADR §140) — 1,875 items across 123 real category
/// groups, already bundled as `creative_items.json` but unloaded by anything before this (named as
/// a deferred gap in ADR §38 back in July, never picked back up until now).
///
/// Block-form entries carry a `block_state_b64` field: a small Bedrock-LE NBT compound with a
/// `network_id` int tag. That value is the exact same one `block_palette.nbt` already uses as
/// Zenith's own block runtime id — verified byte-for-byte against `minecraft:oak_planks` in both
/// files before this loader was written. This means no general multi-state block resolver is
/// needed here: decode the blob, read the tag, done. Pure items (no block form — just a bare `id`
/// string) resolve through the existing <see cref="ItemPalette.Require"/>, the same way
/// <see cref="RecipeRegistry"/>/<see cref="Tools"/> already resolve item names.
/// </summary>
static class CreativeItemsLoader
{
    public const string EmbeddedLogicalName = "creative_items.json";

    public static CreativeItemsData FromEmbeddedResource(ItemPalette itemPalette, System.Reflection.Assembly? assembly = null)
    {
        assembly ??= typeof(CreativeItemsLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedLogicalName)
            ?? throw new InvalidOperationException($"Embedded resource '{EmbeddedLogicalName}' not found.");
        return FromJsonStream(stream, itemPalette);
    }

    public static CreativeItemsData FromJsonStream(Stream stream, ItemPalette itemPalette)
    {
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        if (!root.TryGetProperty("groups", out var groupsEl) || groupsEl.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("creative_items.json missing array 'groups'.");
        if (!root.TryGetProperty("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("creative_items.json missing array 'items'.");

        var groups = new List<CreativeGroupData>(groupsEl.GetArrayLength());
        foreach (var g in groupsEl.EnumerateArray())
        {
            var category = g.GetProperty("creative_category").GetInt32();
            var name = g.GetProperty("name").GetString() ?? "";
            var icon = ResolveIdentity(g.GetProperty("icon"), itemPalette);
            groups.Add(new CreativeGroupData(category, name, icon));
        }

        var items = new List<CreativeItemData>(itemsEl.GetArrayLength());
        foreach (var it in itemsEl.EnumerateArray())
        {
            var id = ResolveIdentity(it, itemPalette);
            var groupIndex = it.GetProperty("group_index").GetInt32();
            if (groupIndex < 0 || groupIndex >= groups.Count)
                throw new InvalidOperationException($"creative_items entry has out-of-range group_index {groupIndex}.");
            items.Add(new CreativeItemData(id, groupIndex));
        }

        if (items.Count == 0)
            throw new InvalidOperationException("creative_items.json has zero items.");

        return new CreativeItemsData(groups, items);
    }

    private static StackId ResolveIdentity(JsonElement entry, ItemPalette itemPalette)
    {
        var name = entry.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("creative_items entry missing 'id'.");

        // Real vanilla data: some group icons are the sentinel "minecraft:empty" — the group has no
        // icon of its own and the client falls back to the first item in the group. Not a real item.
        if (name == "minecraft:empty")
            return default;

        if (!entry.TryGetProperty("block_state_b64", out var b64El))
            return StackId.FromItem(itemPalette.Require(name));

        var blob = Convert.FromBase64String(b64El.GetString()
            ?? throw new InvalidOperationException($"creative_items '{name}' has a null block_state_b64."));
        var decoded = NbtCodec.Decode(blob, NbtEncoding.LittleEndian);
        if (!decoded.Root.Tag.TryGetCompound(out var compound) ||
            !compound.TryGet("network_id", out var networkIdTag) ||
            !networkIdTag.TryGetInt(out var networkId))
            throw new InvalidOperationException($"creative_items block_state_b64 for '{name}' has no network_id tag.");

        return StackId.FromBlock(networkId);
    }
}
