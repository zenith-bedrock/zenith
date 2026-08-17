namespace Zenith.World;

/// <summary>
/// Default <see cref="IBlockRegistry"/> implementation. A thin wrapper delegating to the static
/// <see cref="Blocks"/> façade — same underlying palette state, zero behavioral divergence.
/// See <see cref="IBlockRegistry"/> for why this exists instead of owning its own state.
/// </summary>
sealed class BlockRegistry : IBlockRegistry
{
    public int Air => Blocks.Air;
    public int Stone => Blocks.Stone;
    public int GrassBlock => Blocks.GrassBlock;
    public int Dirt => Blocks.Dirt;
    public int OakPlanks => Blocks.OakPlanks;
    public int OakLog => Blocks.OakLog;
    public int OakLeaves => Blocks.OakLeaves;
    public int Sand => Blocks.Sand;
    public int Gravel => Blocks.Gravel;
    public int Bedrock => Blocks.Bedrock;
    public int Water => Blocks.Water;
    public int Cobblestone => Blocks.Cobblestone;
    public int Deepslate => Blocks.Deepslate;
    public int CoalOre => Blocks.CoalOre;
    public int IronOre => Blocks.IronOre;
    public int CopperOre => Blocks.CopperOre;
    public int GoldOre => Blocks.GoldOre;
    public int DiamondOre => Blocks.DiamondOre;
    public int LapisOre => Blocks.LapisOre;
    public int RedstoneOre => Blocks.RedstoneOre;
    public int DeepslateCoalOre => Blocks.DeepslateCoalOre;
    public int DeepslateIronOre => Blocks.DeepslateIronOre;
    public int DeepslateCopperOre => Blocks.DeepslateCopperOre;
    public int DeepslateGoldOre => Blocks.DeepslateGoldOre;
    public int DeepslateDiamondOre => Blocks.DeepslateDiamondOre;
    public int DeepslateLapisOre => Blocks.DeepslateLapisOre;
    public int DeepslateRedstoneOre => Blocks.DeepslateRedstoneOre;
    public int Chest => Blocks.Chest;

    public bool IsChest(int runtimeId) => Blocks.IsChest(runtimeId);
    public bool TryGetChestCardinal(int runtimeId, out string cardinal) => Blocks.TryGetChestCardinal(runtimeId, out cardinal);
    public bool IsPlaceable(int runtimeId) => Blocks.IsPlaceable(runtimeId);
    public bool IsGravity(int runtimeId) => Blocks.IsGravity(runtimeId);
    public bool IsAir(int runtimeId) => Blocks.IsAir(runtimeId);
    public bool IsFluid(int runtimeId) => Blocks.IsFluid(runtimeId);
    public bool BlocksMovement(int runtimeId) => Blocks.BlocksMovement(runtimeId);
    public bool CanSupportGroundActor(int runtimeId) => Blocks.CanSupportGroundActor(runtimeId);
    public bool CanOccupy(int runtimeId) => Blocks.CanOccupy(runtimeId);
    public int ChestForFacing(string cardinalDirection) => Blocks.ChestForFacing(cardinalDirection);
    public bool SameMergeItem(int a, int b) => Blocks.SameMergeItem(a, b);
    public int NormalizeMergeRuntimeId(int runtimeId) => Blocks.NormalizeMergeRuntimeId(runtimeId);
    public bool TryGetName(int runtimeId, out string name) => Blocks.TryGetName(runtimeId, out name);
    public int BreakTicks(int runtimeId) => Blocks.BreakTicks(runtimeId);
    public int BreakTicks(int blockRuntimeId, StackId held) => Blocks.BreakTicks(blockRuntimeId, held);
    public int CrackEventData(int breakTicks) => Blocks.CrackEventData(breakTicks);
}
