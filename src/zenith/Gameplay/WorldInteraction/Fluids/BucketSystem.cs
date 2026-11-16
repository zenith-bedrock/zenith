using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction.Fluids;

/// <summary>
/// Buckets (ADR §171): an empty bucket scoops up the water or lava source (or powder snow block) it is used on, a full
/// one pours its contents into the open cell it is used on. Liquid placed here flows like any other, through
/// <see cref="BlockUpdateSystem"/>. Creative players keep their stack; survival players swap it. The tick that applies it
/// also re-checks reach and that the player still holds what the client said they did, so a stale packet changes nothing.
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

        var clicked = (use.X, use.Y, use.Z);
        var beyond = BlockFaces.Offset(use.X, use.Y, use.Z, use.Face);
        var done = buckets.TryGetFilled(held.Id, out var filled)
            ? TryPour(player, use, filled, clicked, beyond, online)
            : TryScoop(player, use, held, clicked, beyond);
        if (done) return;

        // Put the client's prediction right: it may have drawn the liquid or the empty cell.
        foreach (var (x, y, z) in new[] { clicked, beyond })
            if (world.TryGetBlock(x, y, z, out var actual)) player.Client.World.SendUpdateBlock(x, y, z, actual);
    }

    private bool TryScoop(Player.Player player, in BucketIntent use, InventorySlot held, (int X, int Y, int Z) clicked, (int X, int Y, int Z) beyond)
    {
        // The client aims a bucket through liquid, so the source can be the clicked cell or the one the click faced.
        var cell = clicked;
        if (!TryFindScoop(player, cell, out var filled))
        {
            cell = beyond;
            if (!TryFindScoop(player, cell, out filled)) return false;
        }

        if (player.GameMode != GameMode.Creative && !SwapForFilled(player, use.HotbarSlot, held, filled)) return false;
        if (!world.TrySetBlock(cell.X, cell.Y, cell.Z, World.World.AirRuntimeId)) return false;
        _updates.Add((cell.X, cell.Y, cell.Z, World.World.AirRuntimeId));
        SyncInventory(player);
        return true;
    }

    private bool TryPour(
        Player.Player player, in BucketIntent use, FilledBucket filled,
        (int X, int Y, int Z) clicked, (int X, int Y, int Z) beyond, IReadOnlyList<Player.Player> online)
    {
        // Into the clicked cell when the bucket's contents could take it (a plant, flowing water), else into the cell the click faced.
        var cell = clicked;
        if (!CanPourInto(player, cell, filled))
        {
            cell = beyond;
            if (!CanPourInto(player, cell, filled)) return false;
        }

        if (player.GameMode != GameMode.Creative &&
            !player.Inventory.TrySet(use.HotbarSlot, buckets.Empty, 1)) return false;

        if (world.TryGetBlock(cell.X, cell.Y, cell.Z, out var existing) && !Blocks.IsAir(existing) && !Blocks.IsFluid(existing))
            BlockSpill.Drop(world, players, online, cell.X, cell.Y, cell.Z, existing);
        if (!world.TrySetBlock(cell.X, cell.Y, cell.Z, filled.Block)) return false;
        _updates.Add((cell.X, cell.Y, cell.Z, filled.Block));
        SyncInventory(player);
        return true;
    }

    private bool TryFindScoop(Player.Player player, (int X, int Y, int Z) cell, out FilledBucket filled)
    {
        filled = default;
        return BlockEditSystem.IsWithinReach(player, cell.X, cell.Y, cell.Z) &&
               world.TryGetBlock(cell.X, cell.Y, cell.Z, out var id) && buckets.TryGetScooped(id, out filled);
    }

    private bool CanPourInto(Player.Player player, (int X, int Y, int Z) cell, FilledBucket filled)
    {
        if (!BlockEditSystem.IsWithinReach(player, cell.X, cell.Y, cell.Z) || !world.TryGetBlock(cell.X, cell.Y, cell.Z, out var id))
            return false;

        // A liquid pours into open space or over flowing liquid (never onto a source); anything else only into open space.
        return filled.Fluid != FluidType.None && Blocks.IsFluid(id) ? !FluidBlocks.IsSource(id) : FluidFlow.CanHold(id);
    }

    /// <summary>One empty bucket becomes a full one; from a stack, the full one goes to the inventory (refused when it has no room).</summary>
    private bool SwapForFilled(Player.Player player, int slot, InventorySlot held, FilledBucket filled)
    {
        if (held.Count == 1) return player.Inventory.TrySet(slot, filled.Item, 1);

        var before = player.Inventory.CaptureSnapshot();
        if (player.Inventory.TrySet(slot, held.Id, held.Count - 1) && player.Inventory.TryAdd(filled.Item)) return true;
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
