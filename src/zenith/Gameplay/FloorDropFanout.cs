using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay;

/// <summary>
/// Floor-drop deposit + AddItemActor fan-out (ADR §26 / §73).
/// Protocol stays session-scoped; callers decide recipients.
/// </summary>
static class FloorDropFanout
{
    /// <summary>Q-throw / death loot — ~2s at 20 TPS so the thrower does not instantly re-pickup.</summary>
    public const int PlayerThrowPickupDelay = 40;

    /// <summary>
    /// Deposit at <paramref name="x"/>,<paramref name="y"/>,<paramref name="z"/> or a nearby free cell
    /// when the origin is occupied by a different <see cref="StackId"/>.
    /// </summary>
    public static bool TryDeposit(
        World.World world,
        PlayerManager players,
        IReadOnlyList<Player.Player> online,
        int x,
        int y,
        int z,
        StackId id,
        int count,
        int pickupDelayTicks = FloorDropStore.DefaultPickupDelay,
        int searchRadius = 3)
    {
        if (count <= 0 || id.IsEmpty) return true;

        for (var r = 0; r <= searchRadius; r++)
        {
            for (var dx = -r; dx <= r; dx++)
            {
                for (var dz = -r; dz <= r; dz++)
                {
                    if (r > 0 && Math.Abs(dx) != r && Math.Abs(dz) != r) continue;

                    var cx = x + dx;
                    var cz = z + dz;
                    var entityId = players.AllocateRuntimeId();
                    if (!world.FloorDrops.TryAddOrMerge(
                            cx, y, cz, id, count, entityId, out var deposit, pickupDelayTicks) ||
                        deposit is null)
                        continue;

                    Publish(online, deposit.Value);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Survival death loot: craft UI + bag + cursor → floor near death pose.
    /// Creative = keepInventory (no-op). SoftCap may void remainder after clear.
    /// </summary>
    public static void DumpOnDeath(
        World.World world,
        PlayerManager players,
        IReadOnlyList<Player.Player> online,
        Player.Player player)
    {
        if (player.GameMode == GameMode.Creative) return;

        var ox = (int)MathF.Floor(player.PositionX);
        var oy = (int)MathF.Floor(player.PositionY);
        var oz = (int)MathF.Floor(player.PositionZ);
        // Void death: land loot on surface so it is recoverable after Respawn.
        if (oy < Blocks.FlatMinY)
            oy = (int)MathF.Floor(world.SampleSpawnFeetY(ox, oz));

        DumpCraftUi(world, players, online, player, ox, oy, oz);
        DumpMainInventory(world, players, online, player, ox, oy, oz);
        world.PersistInventory(player);
    }

    private static void DumpCraftUi(
        World.World world,
        PlayerManager players,
        IReadOnlyList<Player.Player> online,
        Player.Player player,
        int ox,
        int oy,
        int oz)
    {
        for (var g = 0; g < PlayerCraftUi.GridSize; g++)
        {
            var slot = player.CraftUi.GetGrid(g);
            if (slot.IsEmpty) continue;
            DepositOrVoid(world, players, online, player, ox, oy, oz, slot.Id, slot.Count);
            _ = player.CraftUi.TrySetGrid(g, InventorySlot.Empty);
        }

        var result = player.CraftUi.Result;
        if (!result.IsEmpty)
        {
            DepositOrVoid(world, players, online, player, ox, oy, oz, result.Id, result.Count);
            _ = player.CraftUi.TrySetResult(InventorySlot.Empty);
        }
    }

    private static void DumpMainInventory(
        World.World world,
        PlayerManager players,
        IReadOnlyList<Player.Player> online,
        Player.Player player,
        int ox,
        int oy,
        int oz)
    {
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
        {
            var slot = player.Inventory.Get(i);
            if (slot.IsEmpty) continue;
            DepositOrVoid(world, players, online, player, ox, oy, oz, slot.Id, slot.Count);
            _ = player.Inventory.TrySet(i, StackId.FromBlock(Blocks.Air), 0);
        }

        var cursor = player.Inventory.Cursor;
        if (!cursor.IsEmpty)
        {
            DepositOrVoid(world, players, online, player, ox, oy, oz, cursor.Id, cursor.Count);
            _ = player.Inventory.TrySet(PlayerInventory.CursorSlot, StackId.FromBlock(Blocks.Air), 0);
        }
    }

    private static void DepositOrVoid(
        World.World world,
        PlayerManager players,
        IReadOnlyList<Player.Player> online,
        Player.Player player,
        int ox,
        int oy,
        int oz,
        StackId id,
        int count)
    {
        if (TryDeposit(world, players, online, ox, oy, oz, id, count, PlayerThrowPickupDelay))
            return;

        player.Session.Context.Logger.Debug(
            $"Death loot SoftCap void for {player.Username}: {id} x{count}");
    }

    public static void Publish(
        IReadOnlyList<Player.Player> online,
        FloorDropStore.DepositResult deposit)
    {
        if (!deposit.Created && !deposit.CountChanged) return;

        var cx = PlayerChunkTracker.BlockToChunk(deposit.X);
        var cz = PlayerChunkTracker.BlockToChunk(deposit.Z);
        var px = deposit.X + 0.5f;
        var py = deposit.Y + 0.125f;
        var pz = deposit.Z + 0.5f;

        foreach (var peer in online)
        {
            // InGame always; PreSpawn joiners only if they Know the column (§14).
            if (!peer.IsInGame && !peer.Chunks.Knows(cx, cz)) continue;

            var entity = peer.Session.Protocol.Entity;
            var item = peer.Session.Protocol.Inventory.DescribeStack(deposit.Id, deposit.Count);
            if (!deposit.Created && deposit.CountChanged)
                entity.SendRemoveActor(deposit.EntityRuntimeId);
            if (item.NetworkId == 0) continue; // invalid/air item crashes Bedrock near player
            entity.SendAddItemActor(deposit.EntityRuntimeId, item, px, py, pz);
        }
    }
}
