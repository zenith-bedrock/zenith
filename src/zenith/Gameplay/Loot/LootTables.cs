using System.Reflection;
using System.Text.Json;
using Zenith.World;

namespace Zenith.Gameplay.Loot;

/// <summary>
/// The loot tables, loaded from the embedded <c>data/loot_tables.json</c>. The format is the BDS behavior pack's
/// (<c>pools</c> / <c>rolls</c> / <c>conditions</c> / <c>entries</c> / <c>set_count</c>) so a table can be checked against the pack it came
/// from, but only the constructs Zenith can honour are accepted: anything else (looting, enchantments, smelting, killer-type
/// conditions) is a load error, never silently ignored. Loading validates every table and fails loudly.
/// </summary>
static class LootTables
{
    public const string ResourceName = "loot_tables.json";

    private static readonly Lazy<IReadOnlyDictionary<string, LootTable>> Loaded = new(Load);

    public static IReadOnlyCollection<LootTable> All => Loaded.Value.Values.ToArray();

    public static LootTable Get(string name) =>
        Loaded.Value.TryGetValue(name, out var table)
            ? table
            : throw new KeyNotFoundException($"No loot table named '{name}' in {ResourceName}.");

    public static bool Contains(string name) => Loaded.Value.ContainsKey(name);

    /// <summary>Boot validation: every item any table can award exists in the item catalog.</summary>
    public static void ValidateItems(ItemPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        foreach (var table in Loaded.Value.Values)
        foreach (var item in table.Items)
            if (!palette.TryGet(item, out _))
                throw new InvalidDataException($"{ResourceName}: table '{table.Name}' awards unknown item '{item}'.");
    }

    internal static IReadOnlyDictionary<string, LootTable> Parse(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var byName = new Dictionary<string, LootTable>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.GetProperty("tables").EnumerateObject())
        {
            var name = property.Name;
            if (string.IsNullOrWhiteSpace(name)) throw Invalid("a table has no name");
            var pools = new List<LootPool>();
            foreach (var pool in property.Value.GetProperty("pools").EnumerateArray())
                pools.Add(ParsePool(name, pool));
            byName.Add(name, new LootTable(name, pools));
        }

        return byName;
    }

    private static LootPool ParsePool(string table, JsonElement pool)
    {
        Reject(table, pool, "rolls", "conditions", "entries");
        var rolls = pool.TryGetProperty("rolls", out var rollsElement) ? ParseRange(table, rollsElement) : LootRange.Exactly(1);
        if (rolls.Min < 0 || rolls.Max < rolls.Min) throw Invalid($"table '{table}' has rolls {rolls.Min}..{rolls.Max}");

        var conditions = new List<LootCondition>();
        if (pool.TryGetProperty("conditions", out var conditionsElement))
            foreach (var condition in conditionsElement.EnumerateArray())
                conditions.Add(ParseCondition(table, condition));

        var entries = new List<LootEntry>();
        foreach (var entry in pool.GetProperty("entries").EnumerateArray())
            entries.Add(ParseEntry(table, entry));
        if (entries.Count == 0) throw Invalid($"table '{table}' has a pool with no entries");
        return new LootPool(rolls, conditions, entries);
    }

    private static LootCondition ParseCondition(string table, JsonElement condition)
    {
        var kind = condition.GetProperty("condition").GetString();
        switch (kind)
        {
            case "killed_by_player":
                Reject(table, condition, "condition");
                return new KilledByPlayerCondition();
            case "random_chance":
                Reject(table, condition, "condition", "chance");
                var chance = condition.GetProperty("chance").GetDouble();
                if (!double.IsFinite(chance) || chance <= 0 || chance > 1) throw Invalid($"table '{table}' has chance {chance}; it must be in (0, 1]");
                return new RandomChanceCondition(chance);
            default:
                throw Invalid($"table '{table}' uses unsupported condition '{kind}'");
        }
    }

    private static LootEntry ParseEntry(string table, JsonElement entry)
    {
        Reject(table, entry, "type", "name", "weight", "functions");
        if (entry.GetProperty("type").GetString() != "item") throw Invalid($"table '{table}' has an entry that is not an item");
        var item = entry.GetProperty("name").GetString();
        if (string.IsNullOrWhiteSpace(item)) throw Invalid($"table '{table}' has an entry with no item name");
        var weight = entry.TryGetProperty("weight", out var weightElement) ? weightElement.GetInt32() : 1;
        if (weight <= 0) throw Invalid($"table '{table}' item '{item}' has weight {weight}; it must be positive");

        var count = LootRange.Exactly(1);
        if (entry.TryGetProperty("functions", out var functions))
            foreach (var function in functions.EnumerateArray())
            {
                var kind = function.GetProperty("function").GetString();
                if (kind != "set_count") throw Invalid($"table '{table}' item '{item}' uses unsupported function '{kind}'");
                Reject(table, function, "function", "count");
                count = ParseRange(table, function.GetProperty("count"));
                if (count.Min < 0 || count.Max < count.Min) throw Invalid($"table '{table}' item '{item}' has count {count.Min}..{count.Max}");
            }

        return new LootEntry(item, weight, count);
    }

    private static LootRange ParseRange(string table, JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => LootRange.Exactly(element.GetInt32()),
        JsonValueKind.Object => new LootRange(element.GetProperty("min").GetInt32(), element.GetProperty("max").GetInt32()),
        _ => throw Invalid($"table '{table}' has a count that is neither a number nor {{min, max}}")
    };

    /// <summary>A field this loader does not know is an error, so a table never carries behaviour that is silently not applied.</summary>
    private static void Reject(string table, JsonElement element, params string[] known)
    {
        foreach (var property in element.EnumerateObject())
            if (Array.IndexOf(known, property.Name) < 0)
                throw Invalid($"table '{table}' has unsupported field '{property.Name}'");
    }

    private static InvalidDataException Invalid(string message) => new($"{ResourceName}: {message}.");

    private static IReadOnlyDictionary<string, LootTable> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        return Parse(stream);
    }
}
