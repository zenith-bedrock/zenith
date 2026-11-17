using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Gameplay.Survival;
using Zenith.World;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// Resolves explicit player-versus-player melee intents on the gameplay tick.
///
/// The handler only publishes the target runtime-id from ItemUseOnActor. This system owns the
/// cross-player reach/lifecycle decision and delegates the authoritative health/death transition
/// to <see cref="PlayerDamage"/>. It intentionally has no target search: MissedSwing and
/// UseClickAir must never select an arbitrary nearby player.
///
/// Attacker-oriented by design (Player Melee Execution Model audit): an attack is one discrete
/// action with an explicit target already carried in the intent, so resolving it is "for each
/// attacker with a pending attack, resolve its one named target" — never "for each target, scan
/// every possible attacker." <see cref="PlayerManager.GetByRuntimeId"/> resolves that target in
/// O(1); a mob id from the same shared RuntimeId space simply doesn't resolve to a player and is
/// left in the mailbox for the owning mob system to consume on its own turn later this tick.
/// Stays on the tick (not a direct handler call) because the pending attack is a network-thread
/// submission that must cross into the single-writer gameplay context and be ordered after
/// <see cref="Zenith.Gameplay.Survival.MovementSystem"/> — reach reads both actors' current-tick
/// authoritative pose, not last-tick's (see registration order/comment in ZenithServer).
/// </summary>
[GameSystem("player-melee")]
sealed class PlayerMeleeSystem : IGameSystem
{
    private readonly PlayerManager _players;
    private readonly ItemPalette _itemPalette;

    public PlayerMeleeSystem(PlayerManager players, ItemPalette itemPalette) { _players = players; _itemPalette = itemPalette; }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        foreach (var attacker in online)
        {
            var reach = EntityHitboxes.MeleeReach(attacker.GameMode);
            var reachSquared = reach * reach;
            // The predicate resolves against the current player index rather than pre-selecting a
            // candidate, so consumption still happens at most once and only for a target this
            // system actually owns (a player) — an untargeted or mob-targeted intent is left
            // exactly as before for its real consumer.
            if (!attacker.Intents.TryConsumeTargetedAttackIntent(
                    id => _players.GetByRuntimeId(id) is not null,
                    out var targetRuntimeId))
            {
                continue;
            }

            if (!attacker.IsInGame || attacker.IsDead)
                continue;

            var target = _players.GetByRuntimeId(targetRuntimeId);
            if (target is null || ReferenceEquals(attacker, target) || !target.IsInGame || target.IsDead)
                continue;

            var dx = target.PositionX - attacker.PositionX;
            var dz = target.PositionZ - attacker.PositionZ;
            if (EntityHitboxes.PlayerStanding(target.PositionX, target.PositionY, target.PositionZ)
                .DistanceSquaredTo(attacker.PositionX, attacker.PositionY + Blocks.PlayerEyeHeight, attacker.PositionZ) > reachSquared)
                continue;

            var result = PlayerDamage.ApplyCore(
                target,
                _players,
                DamageSource.MeleeFrom(attacker.RuntimeId),
                PlayerMeleeDamage.Resolve(attacker, _itemPalette),
                clock.CurrentTick,
                dx, dz);
            result.Conclude(online);
            if (result.Applied)
            {
                HungerSystem.Exhaust(attacker, HungerSystem.AttackExhaustion);
                GearWear.WearHeldWeapon(attacker);
            }
        }
    }
}
