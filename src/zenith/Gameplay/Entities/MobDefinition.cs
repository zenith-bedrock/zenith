namespace Zenith.Gameplay.Entities;

/// <summary>
/// What distinguishes one ECS actor species from another as plain data: its name (used in logs and loot messages), its
/// health, the item it drops and the experience a kill gives. Behaviour (movement, targeting, attacks) is not data and
/// stays in each species' system.
/// </summary>
sealed record MobDefinition(string Name, float MaxHealth, string LootItemName, int KillExperience);

/// <summary>The species definitions, in one place so a balance change is one line. Values are vanilla parity as the original systems documented them.</summary>
static class MobDefinitions
{
    public static readonly MobDefinition Zombie = new("Zombie", 20f, "minecraft:rotten_flesh", 5);
    public static readonly MobDefinition Cow = new("Cow", 10f, "minecraft:beef", 1);
    public static readonly MobDefinition Creeper = new("Creeper", 20f, "minecraft:gunpowder", 5);
    public static readonly MobDefinition Spider = new("Spider", 16f, "minecraft:string", 5);
    public static readonly MobDefinition Skeleton = new("Skeleton", 20f, "minecraft:bone", 5);
    public static readonly MobDefinition Golem = new("Golem", 100f, "minecraft:iron_ingot", 25);
    public static readonly MobDefinition Minecart = new("Minecart", 6f, "minecraft:minecart", 1);
}
