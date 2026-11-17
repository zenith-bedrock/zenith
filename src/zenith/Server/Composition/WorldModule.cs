using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Gameplay.WorldInteraction.Fluids;
using Zenith.Gameplay.WorldInteraction.RandomTicks;
using Zenith.Session;

namespace Zenith.Server.Composition;

/// <summary>Blocks, inventory, survival and chunk streaming: the systems that change the world and publish it.</summary>
sealed class WorldModule : IGameplayModule
{
    public string Name => "world";

    /// <summary>The one system shutdown must reach: it settles in-flight sand and gravel into the world before the final flush (ADR §57).</summary>
    public GravitySystem Gravity { get; private set; } = null!;

    public void Register(ServerServices s)
    {
        Gravity = new GravitySystem(s.World, s.Players, s.EnvironmentPolicies);

        s.Register(new WorldStateSyncSystem(s.World));
        s.Register(new WeatherSystem(s.World));
        s.Register(new BlockDigSystem(s.World));
        s.Register(new BlockEditSystem(s.Players, s.World, s.Durability, s.BlockPolicies, s.Events));
        // Right after edits: a bucket is a block edit with its own item rules; the water it pours flows in block-update below.
        s.Register(new BucketSystem(s.World, s.Players, new BucketItems(s.ItemPalette), s.BlockPolicies, s.Events, s.Durability));
        s.Register(Gravity);
        // After edits and gravity: reacts to everything they wrote this tick (a plant whose soil was just dug, a door without ground).
        s.Register(new BlockUpdateSystem(s.World, s.Players, s.EnvironmentPolicies));
        s.Register(new RandomTickSystem(s.World, RandomTickBehaviors.Default(), s.EnvironmentPolicies));
        s.Register(new FloorDropSystem(s.World));
        s.Register(new ExperienceOrbSystem(s.World));
        s.Register(new InventorySystem(s.Players, s.World, s.Recipes, s.Creative, s.ContainerPolicies));
        // After Inventory: eating also mutates the held stack and must land before Equipment diffs it.
        s.Register(new HungerSystem(s.ItemPalette, s.Players));
        // After movement and hunger: judges where the player ended up this tick (breath, lava).
        s.Register(new AirSupplySystem(s.Players));
        s.Register(new BurnSystem(s.Players, s.World));
        s.Register(new EffectSystem(s.Players, s.Gate));
        // Inventory/blocks may change the selected held stack; replicate the final same-tick state.
        s.Register(new EquipmentSystem());
        s.Register(new ChunkStreamSystem(s.World, s.Diagnostics.Worldgen));
        s.Register(new ChunkResidencySystem(s.World, s.ChunkResidencySweepIntervalTicks));
        // Last: a departure is announced only after everything this tick that finishes a departed player's work has run.
        s.Register(new PresenceSystem(s.Players, s.Events));
    }
}
