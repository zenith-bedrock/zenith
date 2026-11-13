using Zenith.Diagnostics;
using Zenith.Ecs;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Server.Composition;

/// <summary>
/// Everything a gameplay module may depend on, built once by the composition root. Modules take this one object instead of a
/// growing parameter list, and anything several modules share (the spatial index, the mob spawner, the entity stores) lives
/// here, created before any module runs, so no module depends on another having run first for an object to exist.
/// </summary>
sealed record ServerServices(
    GameLoop Loop,
    ServerRuntimeDiagnostics Diagnostics,
    PlayerManager Players,
    World.World World,
    ItemPalette ItemPalette,
    RecipeRegistry Recipes,
    ItemDurability Durability,
    CreativeCatalog Creative,
    EntityRuntime Entities,
    PlayerSpatialIndex PlayerSpatial,
    MobSpawner Spawner,
    int ChunkResidencySweepIntervalTicks)
{
    /// <summary>Registers <paramref name="system"/> on the loop under its diagnostics timing name: the one place that pairs the two.</summary>
    public void Register(IGameSystem system, string name) => Loop.Register(system, Diagnostics.System(name));
}

/// <summary>A feature's worth of game systems. A module constructs its systems and registers them; it does not own the loop.</summary>
interface IGameplayModule
{
    string Name { get; }
    void Register(ServerServices services);
}

/// <summary>
/// The gameplay modules in the order they run each tick. Registration order is execution order, so the order here is a behaviour, and
/// every constraint is written next to the module that has it:
/// <list type="number">
/// <item><see cref="PlayerInputModule"/>: movement first, so sneak and pose are applied before anything that reads them (placing,
/// opening a chest), and the spatial index is rebuilt from this tick's poses before any later system queries it.</item>
/// <item><see cref="EntityModule"/>: mobs, projectiles, spawning. After player input (they react to this tick's player poses), before
/// world edits.</item>
/// <item><see cref="WorldModule"/>: blocks, inventory, survival, streaming. Chunk streaming is last: it publishes what the earlier
/// systems changed this tick.</item>
/// </list>
/// <c>GameplayCompositionTests</c> pins the resulting system order, so reordering this list (or a module's body) is a visible,
/// reviewed change and not an accident.
/// </summary>
static class GameplayComposition
{
    public static IReadOnlyList<IGameplayModule> Modules() => [new PlayerInputModule(), new EntityModule(), new WorldModule()];

    /// <summary>Registers every module in order and returns the world module, which owns the one system shutdown must reach (gravity).</summary>
    public static WorldModule Register(ServerServices services)
    {
        WorldModule? world = null;
        foreach (var module in Modules())
        {
            module.Register(services);
            world ??= module as WorldModule;
        }

        return world ?? throw new InvalidOperationException("The gameplay composition has no world module.");
    }
}
