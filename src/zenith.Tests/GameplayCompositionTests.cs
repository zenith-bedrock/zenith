using Zenith.Ecs;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Server;
using Zenith.Server.Composition;
using Zenith.Session;
using Xunit;

namespace Zenith.Tests;

public sealed class GameplayCompositionTests
{
    private static (GameLoop Loop, WorldModule World, ServerServices Services) Compose()
    {
        var fx = new IntentTestFixture();
        var diagnostics = new ServerRuntimeDiagnostics();
        var loop = new GameLoop(fx.Clock, fx.Players, fx.Context.Logger);
        var services = new ServerServices(
            loop, diagnostics, fx.Players, fx.World, fx.Context.ItemPalette, fx.Context.Recipes, fx.Context.Durability,
            fx.Context.Creative, new EntityRuntime(), new BatStore(), new VillagerStore(), new PlayerSpatialIndex(),
            new MobSpawner(), ChunkResidencySweepIntervalTicks: 200);
        return (loop, GameplayComposition.Register(services), services);
    }

    /// <summary>
    /// The tick order is behaviour (registration order = execution order), so it is pinned here. Changing it should be a decision a reviewer
    /// sees in this list, not a side effect of moving a line.
    /// </summary>
    [Fact]
    public void Systems_run_in_the_documented_order()
    {
        var (loop, _, _) = Compose();

        Type[] expected =
        [
            // player input
            typeof(TimeSyncSystem), typeof(MovementSystem), typeof(PlayerSpatialIndexSystem), typeof(PlayerMeleeSystem), typeof(ChatSystem), typeof(GameModeSystem),
            // entities
            typeof(ZombieSystem), typeof(MinecartSystem), typeof(ProjectileSystem), typeof(SkeletonSystem), typeof(CowSystem), typeof(CreeperSystem),
            typeof(EndermanSystem), typeof(BatSystem), typeof(SpiderSystem), typeof(VillagerSystem), typeof(GolemSystem), typeof(FishSystem),
            typeof(MobSpawner), typeof(NaturalSpawnSystem),
            // world
            typeof(GameRuleSyncSystem), typeof(BlockDigSystem), typeof(BlockEditSystem), typeof(GravitySystem), typeof(FloorDropSystem),
            typeof(InventorySystem), typeof(HungerSystem), typeof(EffectSystem), typeof(EquipmentSystem), typeof(ChunkStreamSystem), typeof(ChunkResidencySystem)
        ];
        Assert.Equal(expected, loop.Systems.Select(system => system.GetType()).ToArray());
    }

    [Fact]
    public void The_world_module_hands_back_the_gravity_system_shutdown_needs()
    {
        var (loop, world, _) = Compose();
        Assert.Contains(world.Gravity, loop.Systems);
    }

    [Fact]
    public void Every_summonable_species_is_registered_by_the_entity_module()
    {
        var (_, _, services) = Compose();
        var names = services.Spawner.Names;
        Assert.Equal(
            new[] { "bat", "cow", "creeper", "enderman", "fish", "iron_golem", "minecart", "skeleton", "spider", "villager", "zombie" },
            names);
    }
}
