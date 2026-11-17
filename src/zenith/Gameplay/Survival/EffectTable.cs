using System.Reflection;
using System.Text.Json;

namespace Zenith.Gameplay.Survival;

enum EffectCategory { Beneficial, Harmful, Neutral }

/// <summary>What an effect does on a schedule or at once: heal or hurt <see cref="Amount"/> (the instant kind doubles per level).</summary>
sealed record EffectAction(bool Heals, DamageCause Cause, float Amount, int IntervalTicks, float Floor)
{
    /// <summary>Whether a periodic action falls on <paramref name="tick"/>: the interval halves with each amplifier level.</summary>
    public bool DueAt(ulong tick, int amplifier) => tick % (ulong)Math.Max(1, IntervalTicks >> amplifier) == 0;

    /// <summary>How much a damaging action may take from a body with <paramref name="health"/> (poison leaves 1; 0 means nothing to do).</summary>
    public float DamageFor(float health) => MathF.Max(0f, MathF.Min(Amount, health - Floor));

    /// <summary>The instant kinds double with each amplifier level.</summary>
    public float InstantAmount(int amplifier) => Amount * (1 << amplifier);
}

/// <summary>One row of <c>data/effects.json</c>.</summary>
sealed record EffectDefinition(
    EffectType Type, string Name, EffectCategory Category,
    IReadOnlyList<(string Attribute, AttributeOperation Operation, float Amount)> Modifiers,
    float Absorption, EffectAction? Periodic, EffectAction? Instant);

/// <summary>
/// The status effects as data, from the embedded <c>data/effects.json</c>. Validated at load: every <see cref="EffectType"/> has exactly one row
/// (and no row names an unknown id), names and ids are unique, and a modifier names an attribute and an operation that exist.
/// </summary>
sealed class EffectTable
{
    public const string ResourceName = "effects.json";
    public const string ModifierPrefix = "effect:";

    private static readonly Lazy<EffectTable> Loaded = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        return Parse(stream);
    });

    public static EffectTable Default => Loaded.Value;

    private readonly Dictionary<EffectType, EffectDefinition> _byType;

    public IEnumerable<EffectDefinition> Definitions => _byType.Values;

    private EffectTable(Dictionary<EffectType, EffectDefinition> byType) => _byType = byType;

    public EffectDefinition Of(EffectType type) => _byType[type];

    public bool TryFind(string name, out EffectDefinition definition) =>
        (definition = _byType.Values.FirstOrDefault(d => d.Name == name)!) is not null;

    public static EffectTable Parse(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var byType = new Dictionary<EffectType, EffectDefinition>();
        foreach (var e in doc.RootElement.GetProperty("effects").EnumerateArray())
        {
            var name = e.GetProperty("name").GetString();
            if (string.IsNullOrWhiteSpace(name)) throw Invalid("an effect has no name");
            var id = e.GetProperty("id").GetInt32();
            if (!Enum.IsDefined(typeof(EffectType), id)) throw Invalid($"'{name}' has id {id}, which no EffectType has");
            var type = (EffectType)id;
            if (byType.ContainsKey(type)) throw Invalid($"id {id} is listed twice");
            if (byType.Values.Any(d => d.Name == name)) throw Invalid($"name '{name}' is listed twice");

            var categoryText = e.GetProperty("category").GetString();
            if (!Enum.TryParse<EffectCategory>(categoryText, ignoreCase: true, out var category)) throw Invalid($"'{name}' has unknown category '{categoryText}'");

            var modifiers = new List<(string, AttributeOperation, float)>();
            if (e.TryGetProperty("modifiers", out var list))
                foreach (var m in list.EnumerateArray())
                {
                    var attribute = m.GetProperty("attribute").GetString() ?? "";
                    if (!Attr.All.Contains(attribute)) throw Invalid($"'{name}' modifies unknown attribute '{attribute}'");
                    var operationText = m.GetProperty("operation").GetString();
                    var operation = operationText switch
                    {
                        "add" => AttributeOperation.Add,
                        "multiply_base" => AttributeOperation.MultiplyBase,
                        "multiply_total" => AttributeOperation.MultiplyTotal,
                        _ => throw Invalid($"'{name}' has unknown operation '{operationText}'")
                    };
                    modifiers.Add((attribute, operation, m.GetProperty("amount").GetSingle()));
                }

            var absorption = e.TryGetProperty("absorption", out var a) ? a.GetSingle() : 0f;
            if (absorption < 0f) throw Invalid($"'{name}' has negative absorption");
            var periodic = e.TryGetProperty("periodic", out var p) ? Action(name, p, periodic: true) : null;
            var instant = e.TryGetProperty("instant", out var i) ? Action(name, i, periodic: false) : null;
            byType[type] = new EffectDefinition(type, name, category, modifiers, absorption, periodic, instant);
        }

        foreach (var type in Enum.GetValues<EffectType>())
            if (!byType.ContainsKey(type)) throw Invalid($"effect {type} ({(int)type}) has no row");
        return new EffectTable(byType);
    }

    private static EffectAction Action(string name, JsonElement node, bool periodic)
    {
        var kind = node.GetProperty("kind").GetString();
        if (kind is not ("heal" or "damage")) throw Invalid($"'{name}' has unknown kind '{kind}'");
        var heals = kind == "heal";
        var cause = DamageCause.Magic;
        if (!heals)
        {
            var causeText = node.GetProperty("cause").GetString();
            if (!Enum.TryParse(causeText, ignoreCase: true, out cause)) throw Invalid($"'{name}' has unknown damage cause '{causeText}'");
        }

        var amount = node.GetProperty("amount").GetSingle();
        if (amount <= 0f) throw Invalid($"'{name}' has non-positive amount");
        var interval = 0;
        if (periodic)
        {
            interval = node.GetProperty("intervalTicks").GetInt32();
            if (interval <= 0) throw Invalid($"'{name}' has non-positive intervalTicks");
        }

        var floor = node.TryGetProperty("floor", out var f) ? f.GetSingle() : 0f;
        return new EffectAction(heals, cause, amount, interval, floor);
    }

    private static InvalidDataException Invalid(string message) => new($"{ResourceName}: {message}.");
}
