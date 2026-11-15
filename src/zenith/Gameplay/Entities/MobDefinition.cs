using System.Reflection;
using System.Text.Json;

using Zenith.Gameplay.Loot;
using Zenith.Gameplay.Ports;
using Zenith.World;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// What distinguishes one ECS actor species from another as plain data: its name (used in logs and loot messages; also the
/// <see cref="MobKind"/> clients are told it is), its
/// health, the loot table it rolls and the experience a kill gives. Behaviour (movement, targeting, attacks) is not data and
/// stays in each species' system.
/// </summary>
sealed record MobDefinition(
    string Name, MobKind Kind, float MaxHealth, string LootTable, int KillExperience,
    int DeathAnimationTicks = MobDefinitions.DefaultDeathAnimationTicks,
    MeleeProfile? Melee = null,
    KnockbackProfile? Knockback = null)
{
    public (float Width, float Height) Dimensions => Kind switch
    {
        MobKind.Zombie or MobKind.Skeleton or MobKind.Villager => (0.6f, 1.9f),
        MobKind.Cow => (0.9f, 1.3f),
        MobKind.Creeper => (0.6f, 1.8f),
        MobKind.Enderman => (0.6f, 2.9f),
        MobKind.Spider => (1.4f, 0.9f),
        MobKind.Golem => (1.4f, 2.7f),
        MobKind.Bat => (0.5f, 0.9f),
        MobKind.Fish => (0.5f, 0.3f),
        MobKind.Minecart => (0.98f, 0.7f),
        _ => throw new InvalidOperationException($"No dimensions for {Kind}.")
    };
}

/// <summary>A species' contact attack on a player: damage per hit, horizontal reach in blocks, and ticks between hits.</summary>
sealed record MeleeProfile(float Damage, float Reach, int CooldownTicks);

/// <summary>
/// The horizontal push a landed hit gives a species: initial speed in blocks/tick (scaled down by <paramref name="Resistance"/>),
/// the per-tick multiplier it fades by, and the resistance (0 none, 1 immune).
/// </summary>
sealed record KnockbackProfile(float Strength, float DecayPerTick, float Resistance)
{
    /// <summary>Initial push speed after resistance; zero means the hit does not move the actor.</summary>
    public float EffectiveStrength => Strength * (1f - Resistance);
}

/// <summary>
/// The species definitions, loaded from the embedded <c>data/mobs.json</c> so a balance change is an edit to a data file
/// and not to a system. The file is hand-authored (vanilla parity as the original systems documented it), not generated
/// from a BDS capture. Loading validates every entry and fails loudly, so a typo cannot become a silent zero.
/// </summary>
static class MobDefinitions
{
    public const string ResourceName = "mobs.json";

    /// <summary>The client's death animation (tip over, red tint, then the poof) runs 20 ticks; the corpse must outlive it.</summary>
    public const int DefaultDeathAnimationTicks = 20;

    private static readonly string KnownKinds = string.Join(", ", Enum.GetNames<MobKind>());

    private static readonly Lazy<IReadOnlyDictionary<string, MobDefinition>> Loaded = new(Load);

    public static IReadOnlyCollection<MobDefinition> All => Loaded.Value.Values.ToArray();

    /// <summary>Boot validation against the same item palette the species constructors will receive.</summary>
    public static void ValidateLoot(ItemPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        foreach (var definition in Loaded.Value.Values)
            if (!LootTables.Contains(definition.LootTable))
                throw new InvalidDataException($"{ResourceName}: '{definition.Name}' references unknown loot table '{definition.LootTable}'.");
        LootTables.ValidateItems(palette);
    }

    public static MobDefinition Get(string name) =>
        Loaded.Value.TryGetValue(name, out var definition)
            ? definition
            : throw new KeyNotFoundException($"No mob definition named '{name}' in {ResourceName}.");

    public static MobDefinition Zombie => Get("Zombie");
    public static MobDefinition Cow => Get("Cow");
    public static MobDefinition Creeper => Get("Creeper");
    public static MobDefinition Spider => Get("Spider");
    public static MobDefinition Skeleton => Get("Skeleton");
    public static MobDefinition Golem => Get("Golem");
    public static MobDefinition Enderman => Get("Enderman");
    public static MobDefinition Fish => Get("Fish");
    public static MobDefinition Minecart => Get("Minecart");
    public static MobDefinition Bat => Get("Bat");
    public static MobDefinition Villager => Get("Villager");

    internal static IReadOnlyDictionary<string, MobDefinition> Parse(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var byName = new Dictionary<string, MobDefinition>(StringComparer.Ordinal);
        foreach (var entry in doc.RootElement.GetProperty("mobs").EnumerateArray())
        {
            var name = entry.GetProperty("name").GetString();
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException($"{ResourceName}: a mob has no name.");
            // The species name is also its wire kind: a name with no MobKind cannot be announced to clients, so it is a data error now.
            if (!Enum.TryParse<MobKind>(name, out var kind)) throw new InvalidDataException($"{ResourceName}: '{name}' is not a known mob kind ({KnownKinds}).");
            var health = entry.GetProperty("maxHealth").GetSingle();
            if (!float.IsFinite(health) || health <= 0f) throw new InvalidDataException($"{ResourceName}: '{name}' has maxHealth {health}; it must be positive.");
            var loot = entry.GetProperty("lootTable").GetString();
            if (string.IsNullOrWhiteSpace(loot)) throw new InvalidDataException($"{ResourceName}: '{name}' has no lootTable.");
            var xp = entry.GetProperty("killExperience").GetInt32();
            if (xp < 0) throw new InvalidDataException($"{ResourceName}: '{name}' has negative killExperience {xp}.");
            var deathTicks = entry.TryGetProperty("deathAnimationTicks", out var deathElement) ? deathElement.GetInt32() : DefaultDeathAnimationTicks;
            if (deathTicks < 0) throw new InvalidDataException($"{ResourceName}: '{name}' has negative deathAnimationTicks {deathTicks}.");
            var melee = entry.TryGetProperty("melee", out var meleeElement) ? ParseMelee(name, meleeElement) : null;
            var knockback = entry.TryGetProperty("knockback", out var knockbackElement) ? ParseKnockback(name, knockbackElement) : null;
            if (!byName.TryAdd(name, new MobDefinition(name, kind, health, loot, xp, deathTicks, melee, knockback)))
                throw new InvalidDataException($"{ResourceName}: '{name}' is defined twice.");
        }

        return byName;
    }

    private static MeleeProfile ParseMelee(string name, JsonElement element)
    {
        var damage = element.GetProperty("damage").GetSingle();
        var reach = element.GetProperty("reach").GetSingle();
        var cooldown = element.GetProperty("cooldownTicks").GetInt32();
        if (!float.IsFinite(damage) || damage <= 0f) throw new InvalidDataException($"{ResourceName}: '{name}' melee damage {damage} must be positive.");
        if (!float.IsFinite(reach) || reach <= 0f) throw new InvalidDataException($"{ResourceName}: '{name}' melee reach {reach} must be positive.");
        if (cooldown <= 0) throw new InvalidDataException($"{ResourceName}: '{name}' melee cooldownTicks {cooldown} must be positive.");
        return new MeleeProfile(damage, reach, cooldown);
    }

    private static KnockbackProfile ParseKnockback(string name, JsonElement element)
    {
        var strength = element.GetProperty("strength").GetSingle();
        var decay = element.GetProperty("decayPerTick").GetSingle();
        var resistance = element.GetProperty("resistance").GetSingle();
        if (!float.IsFinite(strength) || strength <= 0f) throw new InvalidDataException($"{ResourceName}: '{name}' knockback strength {strength} must be positive.");
        if (!float.IsFinite(decay) || decay <= 0f || decay >= 1f) throw new InvalidDataException($"{ResourceName}: '{name}' knockback decayPerTick {decay} must be in (0, 1).");
        if (!float.IsFinite(resistance) || resistance < 0f || resistance > 1f) throw new InvalidDataException($"{ResourceName}: '{name}' knockback resistance {resistance} must be in [0, 1].");
        return new KnockbackProfile(strength, decay, resistance);
    }

    private static IReadOnlyDictionary<string, MobDefinition> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        return Parse(stream);
    }
}
