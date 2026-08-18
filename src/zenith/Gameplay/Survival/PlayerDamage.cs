using Zenith.Packets;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.Session;
using Zenith.World;
using Zenith.Gameplay.Replication;

namespace Zenith.Gameplay.Survival;

internal enum PlayerDamageOutcome { NotApplied, Hurt, Died }

/// <summary>
/// Result of <see cref="PlayerDamage.ApplyCore"/> — the peer-list-independent core of a damage
/// resolution is already done by the time this exists. <see cref="Conclude"/> is the peer-list-
/// dependent tail, chained in the same expression at every call site so it can't be silently
/// skipped the way two independent static calls could be. Deliberately not named "Replicate": for
/// <see cref="PlayerDamageOutcome.Hurt"/> it really is pure notification (safe to reason about as
/// "just packets"), but for <see cref="PlayerDamageOutcome.Died"/> it also runs
/// <see cref="ChestLidFanout.ReleaseOpener"/>/<see cref="FloorDropFanout.TryDropDeathLoot"/> —
/// both bundle a real state mutation (closing the chest, moving inventory into world floor drops)
/// atomically with their notification, by existing design elsewhere in this codebase, and both need
/// <c>online</c> for that same atomic commit-and-publish. A death is not actually finished — loot
/// isn't dropped, an open chest isn't released — until this runs; it is required, not optional,
/// for that outcome. Every current call site chains it immediately, so this is safe today, but the
/// name must not imply "purely cosmetic, safe to skip."
/// </summary>
internal readonly record struct PlayerDamageResult(
    PlayerDamageOutcome Outcome, Player.Player Player, PlayerManager Players)
{
    public bool Applied => Outcome != PlayerDamageOutcome.NotApplied;

    /// <summary>No-ops for <see cref="PlayerDamageOutcome.NotApplied"/> — callers never need an
    /// `if` around this call. Re-reads <c>Player.OpenChest</c>/<c>Player.GameMode</c> fresh (still
    /// valid — <see cref="PlayerDamage.ApplyCore"/> never clears them) instead of threading them
    /// through the result.</summary>
    public void Conclude(IReadOnlyList<Player.Player> online)
    {
        if (Outcome == PlayerDamageOutcome.NotApplied) return;

        PlayerVisibility.RelayHealth(Player, online);
        if (Outcome == PlayerDamageOutcome.Hurt)
        {
            PlayerVisibility.RelayHurt(Player, online);
            return;
        }

        PlayerVisibility.RelayDeath(Player, online);
        var world = Player.Session.Context.World;
        if (Player.OpenChest.HasValue) ChestLidFanout.ReleaseOpener(online, world, Player);
        if (Player.GameMode != GameMode.Creative) _ = FloorDropFanout.TryDropDeathLoot(world, Players, online, Player);
    }
}

/// <summary>One authoritative Player damage transition shared by concrete gameplay causes.</summary>
static class PlayerDamage
{
    // Phase XXIII-B — player-facing knockback never existed at all (real-client finding: "no
    // knockback real, ou está péssimo"). Bedrock's player position is client-authoritative, so the
    // server cannot move the player directly the way mob knockback does (ZombieSystem's
    // ApplyKnockbackImpulse/Motion) — it can only hand the client one SetActorMotion impulse and let
    // the client's own physics integrate it, same mechanism vanilla uses. Magnitudes chosen to match
    // vanilla/PocketMine's base melee knockback feel (PocketMine's EntityDamageByEntityEvent default
    // base knockback ≈ 0.4 horizontal with a fixed upward pop), not measured from a real client.
    private const float KnockbackHorizontal = 0.4f;
    private const float KnockbackVertical = 0.4f;

    /// <summary>
    /// The peer-list-independent core: does this damage land, and what changes as a result.
    /// Deliberately does not take <c>online</c> — "apply 5 damage to Steve" doesn't need to know
    /// about every other online player; only <see cref="PlayerDamageResult.Conclude"/> does. Every
    /// side effect here is self-only (mutates <paramref name="player"/>'s own state, or sends a
    /// packet only to <paramref name="player"/>'s own client) or has no peer-visible component at
    /// all. On death, the loot-drop/chest-release side effects are NOT finished here — see
    /// <see cref="PlayerDamageResult.Conclude"/>'s doc comment for why those genuinely need
    /// <c>online</c> and can't move into this method.
    /// </summary>
    public static PlayerDamageResult ApplyCore(
        Player.Player player, PlayerManager players, DamageSource source, float amount,
        ulong currentTick, float knockbackDirX = 0f, float knockbackDirZ = 0f)
    {
        // Vanilla-parity: Creative players take no damage from ordinary sources (mob melee,
        // projectiles, explosions, player-vs-player). This was never checked at all — a real bug,
        // not a simplification (found via real-client testing, Phase XXIII-B). Void is the one
        // deliberate exception (established Zenith behavior, ADR §73/§40, matches vanilla — even
        // Creative players die falling out of the world) so it bypasses this guard.
        if (player.GameMode == GameMode.Creative && source.Cause != DamageCause.Void)
            return new PlayerDamageResult(PlayerDamageOutcome.NotApplied, player, players);

        var mitigated = ArmorMitigation.Apply(player, source, amount);
        var result = player.ApplyDamage(source, mitigated, currentTick);
        if (!result.WasApplied)
            return new PlayerDamageResult(PlayerDamageOutcome.NotApplied, player, players);

        // Phase XXV — damage exhaustion source (survival only; a Creative death only happens via the
        // deliberate Void exception above, and shouldn't feed the hunger cycle either).
        if (player.GameMode != GameMode.Creative)
            player.Exhaustion += HungerSystem.DamageExhaustion;
        var entity = player.Session.Protocol.Entity;
        var rid = (ulong)player.RuntimeId;
        if (!result.CausedDeath)
        {
            entity.SendPlayerAttributes(player);
            ApplyKnockback(player, entity, knockbackDirX, knockbackDirZ);
            return new PlayerDamageResult(PlayerDamageOutcome.Hurt, player, players);
        }

        if (player.Effects.Count > 0)
        {
            // Death clears every timed effect (vanilla parity); tell the owning client each is gone.
            var expiring = new List<EffectType>(player.Effects.Keys);
            foreach (var type in expiring)
                entity.SendMobEffect(rid, MobEffectPacket.EventRemove, (int)type, 0, false, 0);
            player.ClearEffects();
        }
        if (!player.TryFinalizeDeath()) throw new InvalidOperationException("A newly lethal HealthState did not finalize its death transition.");
        // Phase XXV — vanilla resets experience to zero on death regardless of gamemode (a Creative
        // death only ever happens via the deliberate Void exception above). Previously nothing
        // touched XP on death at all — a real, documented gap from the earlier cross-reference audit.
        player.SetExperience(0, 0);
        entity.SendPlayerAttributes(player);
        entity.SendDeathInfo(player.DeathCause);
        entity.SendRespawnSearching(player.PositionX, player.PositionY + Blocks.PlayerEyeHeight, player.PositionZ, rid);
        return new PlayerDamageResult(PlayerDamageOutcome.Died, player, players);
    }

    /// <summary>
    /// (dirX, dirZ) is any non-zero vector pointing away from the hit's source (attacker or
    /// projectile flight direction); zero (the default) means the caller has no meaningful
    /// direction (Fall/Starve/Magic/Void) and no impulse is sent — vanilla doesn't knock back for
    /// those causes either.
    /// </summary>
    private static void ApplyKnockback(Player.Player player, EntityProtocol entity, float dirX, float dirZ)
    {
        var lengthSquared = dirX * dirX + dirZ * dirZ;
        if (lengthSquared < 0.0001f) return;
        var length = MathF.Sqrt(lengthSquared);
        entity.SendKnockback(
            (ulong)player.RuntimeId,
            dirX / length * KnockbackHorizontal,
            KnockbackVertical,
            dirZ / length * KnockbackHorizontal);
    }
}
