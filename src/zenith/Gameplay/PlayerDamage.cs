using Zenith.Packets;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.Session;
using Zenith.World;

namespace Zenith.Gameplay;

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

    public static bool Apply(
        Player.Player player, PlayerManager players, IReadOnlyList<Player.Player> online, DamageSource source, float amount,
        ulong currentTick, float knockbackDirX = 0f, float knockbackDirZ = 0f)
    {
        // Vanilla-parity: Creative players take no damage from ordinary sources (mob melee,
        // projectiles, explosions, player-vs-player). This was never checked at all — a real bug,
        // not a simplification (found via real-client testing, Phase XXIII-B). Void is the one
        // deliberate exception (established Zenith behavior, ADR §73/§40, matches vanilla — even
        // Creative players die falling out of the world) so it bypasses this guard.
        if (player.GameMode == GameMode.Creative && source.Cause != DamageCause.Void) return false;

        var mitigated = ArmorMitigation.Apply(player, source, amount);
        var result = player.ApplyDamage(source, mitigated, currentTick);
        if (!result.WasApplied) return false;
        // Phase XXV — damage exhaustion source (survival only; a Creative death only happens via the
        // deliberate Void exception above, and shouldn't feed the hunger cycle either).
        if (player.GameMode != GameMode.Creative)
            player.Exhaustion += Systems.HungerSystem.DamageExhaustion;
        var entity = player.Session.Protocol.Entity;
        var rid = (ulong)player.RuntimeId;
        if (!result.CausedDeath)
        {
            entity.SendPlayerAttributes(player);
            PlayerVisibility.RelayHealth(player, online);
            PlayerVisibility.RelayHurt(player, online);
            ApplyKnockback(player, entity, knockbackDirX, knockbackDirZ);
            return true;
        }
        var world = player.Session.Context.World;
        if (player.OpenChest.HasValue) ChestLidFanout.ReleaseOpener(online, world, player);
        if (player.Effects.Count > 0)
        {
            // Death clears every timed effect (vanilla parity); tell the owning client each is gone.
            var expiring = new List<EffectType>(player.Effects.Keys);
            foreach (var type in expiring)
                entity.SendMobEffect(rid, MobEffectPacket.EventRemove, (int)type, 0, false, 0);
            player.ClearEffects();
        }
        if (!player.TryFinalizeDeath()) throw new InvalidOperationException("A newly lethal HealthState did not finalize its death transition.");
        if (player.GameMode != GameMode.Creative) _ = FloorDropFanout.TryDropDeathLoot(world, players, online, player);
        // Phase XXV — vanilla resets experience to zero on death regardless of gamemode (a Creative
        // death only ever happens via the deliberate Void exception above). Previously nothing
        // touched XP on death at all — a real, documented gap from the earlier cross-reference audit.
        player.SetExperience(0, 0);
        entity.SendPlayerAttributes(player);
        PlayerVisibility.RelayHealth(player, online);
        PlayerVisibility.RelayDeath(player, online);
        entity.SendDeathInfo(player.DeathCause);
        entity.SendRespawnSearching(player.PositionX, player.PositionY + Blocks.PlayerEyeHeight, player.PositionZ, rid);
        return true;
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
