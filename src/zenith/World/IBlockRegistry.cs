namespace Zenith.World;

/// <summary>
/// Interface-abstracted view over the block palette and its runtime-id classification predicates.
/// Introduced as an intermediate step for ADR §25 (see decisions log): the default implementation
/// (<see cref="BlockRegistry"/>) delegates to the existing static <see cref="Blocks"/> façade rather
/// than owning its own state, so this does not move any of the façade's call sites — it only gives
/// new code an instance-injectable, test-swappable seam via <see cref="ServerContext"/>.
/// </summary>
interface IBlockRegistry
{
    int Air { get; }
    int Stone { get; }
    int GrassBlock { get; }
    int Dirt { get; }
    int OakPlanks { get; }
    int OakLog { get; }
    int OakLeaves { get; }
    int Sand { get; }
    int Gravel { get; }
    int Bedrock { get; }
    int Water { get; }
    int Cobblestone { get; }
    int Deepslate { get; }
    int CoalOre { get; }
    int IronOre { get; }
    int CopperOre { get; }
    int GoldOre { get; }
    int DiamondOre { get; }
    int LapisOre { get; }
    int RedstoneOre { get; }
    int DeepslateCoalOre { get; }
    int DeepslateIronOre { get; }
    int DeepslateCopperOre { get; }
    int DeepslateGoldOre { get; }
    int DeepslateDiamondOre { get; }
    int DeepslateLapisOre { get; }
    int DeepslateRedstoneOre { get; }
    int Chest { get; }

    bool IsChest(int runtimeId);
    bool TryGetChestCardinal(int runtimeId, out string cardinal);
    bool IsPlaceable(int runtimeId);
    bool IsGravity(int runtimeId);
    bool IsAir(int runtimeId);
    bool IsFluid(int runtimeId);
    bool BlocksMovement(int runtimeId);
    bool CanSupportGroundActor(int runtimeId);
    bool CanOccupy(int runtimeId);
    int ChestForFacing(string cardinalDirection);
    bool SameMergeItem(int a, int b);
    int NormalizeMergeRuntimeId(int runtimeId);
    bool TryGetName(int runtimeId, out string name);
    int BreakTicks(int runtimeId);
    int BreakTicks(int blockRuntimeId, StackId held);
    int CrackEventData(int breakTicks);
}
