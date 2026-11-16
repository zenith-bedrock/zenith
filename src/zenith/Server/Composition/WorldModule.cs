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
        Gravity = new GravitySystem(s.World, s.Players);

        s.Register(new GameRuleSyncSystem(s.World), "game-rule-sync");
        s.Register(new BlockDigSystem(s.World), "block-dig");
        s.Register(new BlockEditSystem(s.Players, s.World, s.Durability, s.BlockPolicies, s.Events), "block-edit");
        // Right after edits: a bucket is a block edit with its own item rules; the water it pours flows in block-update below.
        s.Register(new BucketSystem(s.World, s.Players, new BucketItems(s.ItemPalette)), "bucket");
        s.Register(Gravity, "gravity");
        // After edits and gravity: reacts to everything they wrote this tick (a plant whose soil was just dug, a door without ground).
        s.Register(new BlockUpdateSystem(s.World, s.Players), "block-update");
        s.Register(new RandomTickSystem(s.World, RandomTickBehaviors.Default()), "random-tick");
        s.Register(new FloorDropSystem(s.World), "floor-drop");
        s.Register(new InventorySystem(s.Players, s.World, s.Recipes, s.Creative, s.ContainerPolicies), "inventory");
        // After Inventory: eating also mutates the held stack and must land before Equipment diffs it.
        s.Register(new HungerSystem(s.ItemPalette, s.Players), "hunger");
        // After movement and hunger: judges where the player ended up this tick (breath, lava).
        s.Register(new AirSupplySystem(s.Players), "air-supply");
        s.Register(new EffectSystem(s.Players, s.Gate), "effect");
        // Inventory/blocks may change the selected held stack; replicate the final same-tick state.
        s.Register(new EquipmentSystem(), "equipment");
        s.Register(new ChunkStreamSystem(s.World, s.Diagnostics.Worldgen), "chunk-stream");
        s.Register(new ChunkResidencySystem(s.World, s.ChunkResidencySweepIntervalTicks), "chunk-residency");
        // Last: a departure is announced only after everything this tick that finishes a departed player's work has run.
        s.Register(new PresenceSystem(s.Players, s.Events), "presence");
    }
}
