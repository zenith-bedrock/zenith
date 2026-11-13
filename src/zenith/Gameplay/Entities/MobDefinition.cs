using System.Reflection;
using System.Text.Json;

using Zenith.Gameplay.Ports;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// What distinguishes one ECS actor species from another as plain data: its name (used in logs and loot messages; also the
/// <see cref="MobKind"/> clients are told it is), its
/// health, the item it drops and the experience a kill gives. Behaviour (movement, targeting, attacks) is not data and
/// stays in each species' system.
/// </summary>
sealed record MobDefinition(string Name, MobKind Kind, float MaxHealth, string LootItemName, int KillExperience);

/// <summary>
/// The species definitions, loaded from the embedded <c>data/mobs.json</c> so a balance change is an edit to a data file
/// and not to a system. The file is hand-authored (vanilla parity as the original systems documented it), not generated
/// from a BDS capture. Loading validates every entry and fails loudly, so a typo cannot become a silent zero.
/// </summary>
static class MobDefinitions
{
    public const string ResourceName = "mobs.json";

    private static readonly string KnownKinds = string.Join(", ", Enum.GetNames<MobKind>());

    private static readonly Lazy<IReadOnlyDictionary<string, MobDefinition>> Loaded = new(Load);

    public static IReadOnlyCollection<MobDefinition> All => Loaded.Value.Values.ToArray();

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
            var loot = entry.GetProperty("lootItem").GetString();
            if (string.IsNullOrWhiteSpace(loot)) throw new InvalidDataException($"{ResourceName}: '{name}' has no lootItem.");
            var xp = entry.GetProperty("killExperience").GetInt32();
            if (xp < 0) throw new InvalidDataException($"{ResourceName}: '{name}' has negative killExperience {xp}.");
            if (!byName.TryAdd(name, new MobDefinition(name, kind, health, loot, xp)))
                throw new InvalidDataException($"{ResourceName}: '{name}' is defined twice.");
        }

        return byName;
    }

    private static IReadOnlyDictionary<string, MobDefinition> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        return Parse(stream);
    }
}
