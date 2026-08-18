using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Session;
using Zenith.World;
using Zenith.Gameplay.Replication;

namespace Zenith.Gameplay.WorldInteraction;

/// <summary>
/// Owns the authoritative dig lifecycle on the gameplay tick: start, abort, held-tool retarget
/// and idle expiry. It prepares dig authorization consumed later by <see cref="BlockEditSystem"/>.
/// </summary>
sealed class BlockDigSystem : IGameSystem
{
    private readonly World.World _world;

    public BlockDigSystem(World.World world) => _world = world;

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        if (online.Count == 0) return;

        foreach (var player in online)
        {
            if (!player.IsInGame)
            {
                while (player.TryConsumeDig(out _)) { }
                continue;
            }

            while (player.TryConsumeDig(out var dig))
                ApplyDig(player, dig, clock, online);
            UpdateDigToolIfHeldChanged(player, clock, online);
            AbortIdleDigIfStale(player, clock, online);
        }
    }

    /// <summary>Dispatches one dig intent to its matching branch. Each branch owns its own
    /// mutation and notification together (e.g. "abort" always pairs with "stop crack") — there is
    /// no decision/replication split to extract here, only a readable name per intent.</summary>
    private void ApplyDig(
        global::Zenith.Player.Player player,
        in DigIntent dig,
        GameClock clock,
        IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _ = clock;
        if (player.IsDead) return;

        if (dig.IsActivity) { ApplyActivityPing(player, dig); return; }
        if (dig.IsAbort) { ApplyAbort(player, dig, online); return; }
        if (player.IsBreakTarget(dig.X, dig.Y, dig.Z)) return;

        ApplyNewBreak(player, dig, online);
    }

    private static void ApplyActivityPing(global::Zenith.Player.Player player, in DigIntent dig)
    {
        if (player.IsBreakTarget(dig.X, dig.Y, dig.Z))
            player.MarkDigActive(dig.StartedTick);
    }

    private static void ApplyAbort(
        global::Zenith.Player.Player player, in DigIntent dig, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        BlockCrackFanout.Stop(online, player.Session, dig.X, dig.Y, dig.Z);
        if (player.IsBreakTarget(dig.X, dig.Y, dig.Z))
            player.AbortBreak();
    }

    private void ApplyNewBreak(
        global::Zenith.Player.Player player, in DigIntent dig, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        if (player.TryGetBreakState(out var activeBreak))
            BlockCrackFanout.Stop(online, player.Session, activeBreak.X, activeBreak.Y, activeBreak.Z);

        player.BeginBreak(dig.X, dig.Y, dig.Z, dig.StartedTick, dig.RequiredTicks, dig.HeldStackId);
        if (dig.RequiredTicks > 0)
        {
            BlockCrackFanout.Start(online, player.Session, dig.X, dig.Y, dig.Z, dig.RequiredTicks);
            var hitBlock = _world.GetBlock(dig.X, dig.Y, dig.Z);
            if (hitBlock != World.World.AirRuntimeId)
                BlockSoundFanout.Hit(online, player.Session, dig.X, dig.Y, dig.Z, hitBlock);
        }
        PlayerVisibility.RelaySwingArm(player, online, swingSource: "mine");
    }

    /// <summary>Mid-dig held tool change preserves progress and updates the crack speed (§27).</summary>
    private void UpdateDigToolIfHeldChanged(
        global::Zenith.Player.Player player,
        GameClock clock,
        IReadOnlyList<global::Zenith.Player.Player> online)
    {
        if (!player.TryGetBreakState(out var activeBreak) || player.IsDead) return;
        if (player.GameMode == GameMode.Creative) return;

        var held = player.Inventory.Get(player.SelectedHotbarSlot);
        var heldId = held.IsEmpty ? default : held.Id;
        if (heldId == activeBreak.HeldStackId) return;

        var block = _world.GetBlock(activeBreak.X, activeBreak.Y, activeBreak.Z);
        var oldNeed = activeBreak.RequiredTicks;
        var newNeed = Blocks.BreakTicks(block, heldId);
        if (newNeed < 0)
        {
            BlockCrackFanout.Stop(online, player.Session, activeBreak.X, activeBreak.Y, activeBreak.Z);
            player.AbortBreak();
            return;
        }

        var now = clock.CurrentTick;
        var elapsed = now >= activeBreak.StartedTick ? now - activeBreak.StartedTick : 0ul;
        var progress = oldNeed > 0 ? Math.Clamp(elapsed / (double)oldNeed, 0.0, 1.0) : 1.0;
        var newStarted = newNeed <= 0 ? now : now - (ulong)Math.Round(progress * newNeed);

        player.RetargetBreakTiming(newStarted, newNeed, heldId);
        player.MarkDigActive(now);

        if (Blocks.CrackEventData(oldNeed) != Blocks.CrackEventData(newNeed) && newNeed > 0)
            BlockCrackFanout.UpdateSpeed(
                online, player.Session, activeBreak.X, activeBreak.Y, activeBreak.Z, newNeed);
    }

    /// <summary>
    /// Stops stale crack animation only after its authorized dig window and the idle grace period.
    /// </summary>
    private static void AbortIdleDigIfStale(
        global::Zenith.Player.Player player,
        GameClock clock,
        IReadOnlyList<global::Zenith.Player.Player> online)
    {
        if (!player.TryGetBreakState(out var activeBreak) || player.IsDead) return;
        if (clock.CurrentTick < activeBreak.LastActivityTick) return;

        var digWindowEnd = activeBreak.StartedTick + (ulong)Math.Max(activeBreak.RequiredTicks, 0);
        if (clock.CurrentTick < digWindowEnd ||
            clock.CurrentTick - activeBreak.LastActivityTick < Player.Player.DigIdleAbortTicks)
            return;

        BlockCrackFanout.Stop(online, player.Session, activeBreak.X, activeBreak.Y, activeBreak.Z);
        player.AbortBreak();
    }
}
