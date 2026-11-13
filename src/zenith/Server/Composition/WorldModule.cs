using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction;
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
        s.Register(new BlockEditSystem(s.Players, s.World, s.Durability), "block-edit");
        s.Register(Gravity, "gravity");
        s.Register(new FloorDropSystem(s.World), "floor-drop");
        s.Register(new InventorySystem(s.Players, s.World, s.Recipes, s.Creative), "inventory");
        // After Inventory: eating also mutates the held stack and must land before Equipment diffs it.
        s.Register(new HungerSystem(s.ItemPalette, s.Players), "hunger");
        s.Register(new EffectSystem(s.Players), "effect");
        // Inventory/blocks may change the selected held stack; replicate the final same-tick state.
        s.Register(new EquipmentSystem(), "equipment");
        s.Register(new ChunkStreamSystem(s.World, s.Diagnostics.Worldgen), "chunk-stream");
        s.Register(new ChunkResidencySystem(s.World, s.ChunkResidencySweepIntervalTicks), "chunk-residency");
    }
}
