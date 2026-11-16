using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction.Fluids;

/// <summary>
/// Buckets (ADR §171): an empty bucket scoops up the water source it is used on, a water bucket pours a source into the
/// open cell it is used on. Water placed here flows like any other, through <see cref="BlockUpdateSystem"/>. Creative
/// players keep their stack; survival players swap it. The tick that applies it also re-checks reach and that the player
/// still holds what the client said they did, so a stale packet changes nothing.
/// </summary>
sealed class BucketSystem(World.World world, PlayerManager players, BucketItems buckets) : IGameSystem
{
    private readonly List<(int X, int Y, int Z, int BlockRuntimeId)> _updates = [];

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        _updates.Clear();
        foreach (var player in online)
        {
            while (player.Intents.TryConsumeBucket(out var use))
            {
                if (player.IsInGame && !player.IsDead) Apply(player, use, online);
            }
        }

        if (_updates.Count == 0) return;
        foreach (var peer in online)
            if (peer.IsInGame) peer.Client.World.PublishUpdateBlocks(_updates);
    }

    private void Apply(Player.Player player, in BucketIntent use, IReadOnlyList<Player.Player> online)
    {
        var held = PlayerInventory.IsValidHotbarSlot(use.HotbarSlot) ? player.Inventory.Get(use.HotbarSlot) : InventorySlot.Empty;
        if (held.IsEmpty || held.Id != use.ExpectedStack || !buckets.IsBucket(held.Id)) return;

        var (fx, fy, fz) = BlockFaces.Offset(use.X, use.Y, use.Z, use.Face);
        var done = held.Id == buckets.Water
            ? TryPour(player, use, held, (use.X, use.Y, use.Z), (fx, fy, fz), online)
            : TryScoop(player, use, held, (use.X, use.Y, use.Z), (fx, fy, fz));
        if (!done)
        {
            // Put the client's prediction right: it may have drawn the water or the empty cell.
            foreach (var (x, y, z) in new[] { (use.X, use.Y, use.Z), (fx, fy, fz) })
                if (world.TryGetBlock(x, y, z, out var actual)) player.Client.World.SendUpdateBlock(x, y, z, actual);
        }
    }

    private bool TryScoop(Player.Player player, in BucketIntent use, InventorySlot held, (int X, int Y, int Z) clicked, (int X, int Y, int Z) beyond)
    {
        // The client aims a bucket through water, so the source can be the clicked cell or the one the click faced.
        var source = IsSourceInReach(player, clicked) ? clicked : IsSourceInReach(player, beyond) ? beyond : ((int, int, int)?)null;
        if (source is not var (x, y, z)) return false;

        if (player.GameMode != GameMode.Creative && !SwapForWaterBucket(player, use.HotbarSlot, held)) return false;
        if (!world.TrySetBlock(x, y, z, World.World.AirRuntimeId)) return false;
        _updates.Add((x, y, z, World.World.AirRuntimeId));
        SyncInventory(player);
        return true;
    }

    private bool TryPour(
        Player.Player player, in BucketIntent use, InventorySlot held,
        (int X, int Y, int Z) clicked, (int X, int Y, int Z) beyond, IReadOnlyList<Player.Player> online)
    {
        // Into the clicked cell when water could take it (a plant, flowing water), else into the cell the click faced.
        var target = CanPourInto(player, clicked) ? clicked : CanPourInto(player, beyond) ? beyond : ((int, int, int)?)null;
        if (target is not var (x, y, z)) return false;

        if (player.GameMode != GameMode.Creative &&
            !player.Inventory.TrySet(use.HotbarSlot, buckets.Empty, 1)) return false;

        if (world.TryGetBlock(x, y, z, out var existing) && !Blocks.IsAir(existing) && !WaterFlow.IsWater(existing))
            BlockSpill.Drop(world, players, online, x, y, z, existing);
        if (!world.TrySetBlock(x, y, z, Blocks.Water)) return false;
        _updates.Add((x, y, z, Blocks.Water));
        SyncInventory(player);
        return true;
    }

    private bool IsSourceInReach(Player.Player player, (int X, int Y, int Z) cell) =>
        BlockEditSystem.IsWithinReach(player, cell.X, cell.Y, cell.Z) &&
        world.TryGetBlock(cell.X, cell.Y, cell.Z, out var id) && WaterFlow.IsSource(id);

    private bool CanPourInto(Player.Player player, (int X, int Y, int Z) cell) =>
        BlockEditSystem.IsWithinReach(player, cell.X, cell.Y, cell.Z) &&
        world.TryGetBlock(cell.X, cell.Y, cell.Z, out var id) && WaterFlow.CanTakeSource(id);

    /// <summary>One empty bucket becomes a water bucket; from a stack, the full one goes to the inventory (refused when it has no room).</summary>
    private bool SwapForWaterBucket(Player.Player player, int slot, InventorySlot held)
    {
        if (held.Count == 1) return player.Inventory.TrySet(slot, buckets.Water, 1);

        var before = player.Inventory.CaptureSnapshot();
        if (player.Inventory.TrySet(slot, held.Id, held.Count - 1) && player.Inventory.TryAdd(buckets.Water)) return true;
        player.Inventory.RestoreSnapshot(before);
        return false;
    }

    private void SyncInventory(Player.Player player)
    {
        if (player.GameMode == GameMode.Creative) return;
        player.Client.Inventory.SendInventoryContent(player.Inventory);
        world.PersistInventory(player);
    }
}
