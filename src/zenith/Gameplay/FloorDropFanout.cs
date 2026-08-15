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

    /// <summary>One authoritative floor-drop request, committed as part of a larger gameplay operation.</summary>
    public readonly record struct DepositRequest(StackId Id, int Count);

    private readonly record struct PlannedDeposit(int X, int Y, int Z, StackId Id, int Count);

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
        return TryDepositBatch(
            world, players, online, x, y, z,
            [new DepositRequest(id, count)], pickupDelayTicks, searchRadius);
    }

    /// <summary>
    /// Checks whether a concrete drop can be committed without mutating state. A lethal gameplay
    /// transition may use this before it removes its source so a bounded floor store cannot turn
    /// an otherwise valid loot decision into silent item loss.
    /// </summary>
    public static bool CanDeposit(
        World.World world,
        int x,
        int y,
        int z,
        StackId id,
        int count,
        int searchRadius = 3) =>
        TryPlanDeposits(world.FloorDrops, x, y, z, [new DepositRequest(id, count)], searchRadius, out _);

    /// <summary>
    /// Checks a concrete sequence of drops as one operation without changing world state. Callers
    /// use it before removing a source whose contents may need several nearby floor cells.
    /// </summary>
    public static bool CanDepositBatch(
        World.World world,
        int x,
        int y,
        int z,
        IReadOnlyList<DepositRequest> requests,
        int searchRadius = 3) =>
        TryPlanDeposits(world.FloorDrops, x, y, z, requests, searchRadius, out _);

    /// <summary>
    /// Plans every deposit before mutating or publishing any of them. This keeps an inventory
    /// transaction that drops several stacks atomic: a full/blocked floor-drop area rejects the
    /// whole operation rather than leaving an earlier visible drop behind.
    /// </summary>
    public static bool TryDepositBatch(
        World.World world,
        PlayerManager players,
        IReadOnlyList<Player.Player> online,
        int x,
        int y,
        int z,
        IReadOnlyList<DepositRequest> requests,
        int pickupDelayTicks = FloorDropStore.DefaultPickupDelay,
        int searchRadius = 3,
        float velocityX = 0f,
        float velocityY = 0f,
        float velocityZ = 0f)
    {
        if (requests.Count == 0) return true;
        if (!TryPlanDeposits(world.FloorDrops, x, y, z, requests, searchRadius, out var plan))
            return false;

        var publications = new List<FloorDropStore.DepositResult>(plan.Count);
        var snapshot = world.FloorDrops.CaptureState();
        try
        {
            foreach (var entry in plan)
            {
                if (!world.FloorDrops.TryAddOrMerge(
                        entry.X, entry.Y, entry.Z, entry.Id, entry.Count,
                        players.AllocateRuntimeId(), out var deposit, pickupDelayTicks) ||
                    deposit is null)
                {
                    // The plan and commit both run on the gameplay owner. Reaching this means a
                    // FloorDropStore invariant changed; restore every earlier deposit before
                    // surfacing the violated invariant to the loop owner.
                    throw new InvalidOperationException("Floor-drop batch plan could not commit.");
                }

                publications.Add(deposit.Value);
            }
        }
        catch
        {
            world.FloorDrops.RestoreState(snapshot);
            throw;
        }

        foreach (var deposit in publications)
            Publish(online, deposit, velocityX, velocityY, velocityZ);
        return true;
    }

    private static bool TryPlanDeposits(
        FloorDropStore store,
        int x,
        int y,
        int z,
        IReadOnlyList<DepositRequest> requests,
        int searchRadius,
        out List<PlannedDeposit> plan)
    {
        var projected = new Dictionary<(int X, int Y, int Z), (StackId Id, int Count)>();
        foreach (var drop in store.Snapshot())
            projected[drop.Pos] = (drop.Id, drop.Count);

        plan = new List<PlannedDeposit>(requests.Count);
        foreach (var request in requests)
        {
            if (request.Count <= 0 || request.Id.IsEmpty) continue;
            if (request.Count > FloorDropStore.MaxStack) return false;

            var planned = false;
            for (var r = 0; r <= searchRadius && !planned; r++)
            {
                for (var dx = -r; dx <= r && !planned; dx++)
                {
                    for (var dz = -r; dz <= r; dz++)
                    {
                        if (r > 0 && Math.Abs(dx) != r && Math.Abs(dz) != r) continue;

                        var key = (x + dx, y, z + dz);
                        if (projected.TryGetValue(key, out var existing))
                        {
                            if (existing.Id != request.Id || existing.Count > FloorDropStore.MaxStack - request.Count)
                                continue;

                            projected[key] = (existing.Id, existing.Count + request.Count);
                        }
                        else
                        {
                            if (projected.Count >= FloorDropStore.SoftCap) continue;
                            projected[key] = (request.Id, request.Count);
                        }

                        plan.Add(new PlannedDeposit(key.Item1, key.Item2, key.Item3, request.Id, request.Count));
                        planned = true;
                        break;
                    }
                }
            }

            if (!planned)
            {
                plan.Clear();
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Survival death loot: craft UI + bag + cursor → floor near death pose. Every source stack
    /// is planned before any inventory slot is cleared, so a capacity refusal keeps all of the
    /// player's authoritative inventory instead of producing a partial death drop.
    /// </summary>
    public static bool TryDropDeathLoot(
        World.World world,
        PlayerManager players,
        IReadOnlyList<Player.Player> online,
        Player.Player player)
    {
        if (player.GameMode == GameMode.Creative) return true;

        var ox = (int)MathF.Floor(player.PositionX);
        var oy = (int)MathF.Floor(player.PositionY);
        var oz = (int)MathF.Floor(player.PositionZ);
        // Void death: land loot on surface so it is recoverable after Respawn.
        if (oy < Blocks.FlatMinY)
            oy = (int)MathF.Floor(world.SampleSpawnFeetY(ox, oz));

        var requests = CollectDeathLoot(player);
        if (!TryDepositBatch(world, players, online, ox, oy, oz, requests, PlayerThrowPickupDelay))
        {
            player.Session.Context.Logger.Debug(
                $"Death loot refused for {player.Username}: floor-drop capacity could not accept the complete inventory.");
            return false;
        }

        player.CraftUi.Clear();
        player.Inventory.Clear();
        world.PersistInventory(player);
        world.PersistArmor(player);
        ArmorFanout.Broadcast(player, online);
        return true;
    }

    private static List<DepositRequest> CollectDeathLoot(Player.Player player)
    {
        var requests = new List<DepositRequest>(
            PlayerCraftUi.GridSize + PlayerInventory.FullInventorySize + PlayerInventory.ArmorSize + 2);
        for (var g = 0; g < PlayerCraftUi.GridSize; g++)
        {
            var slot = player.CraftUi.GetGrid(g);
            if (!slot.IsEmpty)
                requests.Add(new DepositRequest(slot.Id, slot.Count));
        }

        var result = player.CraftUi.Result;
        if (!result.IsEmpty)
            requests.Add(new DepositRequest(result.Id, result.Count));

        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
        {
            var slot = player.Inventory.Get(i);
            if (!slot.IsEmpty)
                requests.Add(new DepositRequest(slot.Id, slot.Count));
        }

        var cursor = player.Inventory.Cursor;
        if (!cursor.IsEmpty)
            requests.Add(new DepositRequest(cursor.Id, cursor.Count));

        for (var a = 0; a < PlayerInventory.ArmorSize; a++)
        {
            var armor = player.Inventory.GetArmor(a);
            if (!armor.IsEmpty)
                requests.Add(new DepositRequest(armor.Id, armor.Count));
        }

        return requests;
    }

    /// <summary>
    /// <paramref name="velocityX"/>/<paramref name="velocityY"/>/<paramref name="velocityZ"/> only
    /// apply to a genuinely new cell (a fresh toss) — a merge/republish is already-settled loot
    /// getting topped off or re-announced, not a new throw, so it always renders static.
    /// </summary>
    public static void Publish(
        IReadOnlyList<Player.Player> online,
        FloorDropStore.DepositResult deposit,
        float velocityX = 0f,
        float velocityY = 0f,
        float velocityZ = 0f)
    {
        if (!deposit.Created && !deposit.CountChanged) return;

        var cx = PlayerChunkTracker.BlockToChunk(deposit.X);
        var cz = PlayerChunkTracker.BlockToChunk(deposit.Z);
        var px = deposit.X + 0.5f;
        var py = deposit.Y + 0.125f;
        var pz = deposit.Z + 0.5f;
        if (!deposit.Created)
            velocityX = velocityY = velocityZ = 0f;

        foreach (var peer in online)
        {
            // InGame always; PreSpawn joiners only if they Know the column (§14).
            if (!peer.IsInGame && !peer.Chunks.Knows(cx, cz)) continue;

            var entity = peer.Session.Protocol.Entity;
            if (!deposit.Created && deposit.CountChanged)
                entity.SendRemoveActor(deposit.EntityRuntimeId);
            entity.SendFloorDropActor(
                deposit.EntityRuntimeId, deposit.Id, deposit.Count, px, py, pz,
                velocityX, velocityY, velocityZ);
        }
    }
}
