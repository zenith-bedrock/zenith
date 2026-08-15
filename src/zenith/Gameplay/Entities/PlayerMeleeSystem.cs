using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Gameplay.Survival;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// Resolves explicit player-versus-player melee intents on the gameplay tick.
///
/// The handler only publishes the target runtime-id from ItemUseOnActor. This system owns the
/// cross-player reach/lifecycle decision and delegates the authoritative health/death transition
/// to <see cref="PlayerDamage"/>. It intentionally has no target search: MissedSwing and
/// UseClickAir must never select an arbitrary nearby player.
/// </summary>
sealed class PlayerMeleeSystem : IGameSystem
{
    private const float AttackDistance = 2.25f;
    private const float AttackDamage = 4f;

    private readonly PlayerManager _players;

    public PlayerMeleeSystem(PlayerManager players) => _players = players;

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        var reachSquared = AttackDistance * AttackDistance;

        // Resolve by the wire runtime-id rather than snapshot/list order. Each attacker has one
        // overwrite-latest intent, so an accepted intent is consumed exactly once this tick.
        foreach (var target in online)
        {
            foreach (var attacker in online)
            {
                if (!attacker.TryConsumeTargetedAttackIntent(target.RuntimeId))
                    continue;

                if (ReferenceEquals(attacker, target) ||
                    !attacker.IsInGame || attacker.IsDead ||
                    !target.IsInGame || target.IsDead)
                {
                    continue;
                }

                var dx = target.PositionX - attacker.PositionX;
                var dz = target.PositionZ - attacker.PositionZ;
                if (dx * dx + dz * dz > reachSquared)
                    continue;

                _ = PlayerDamage.Apply(
                    target,
                    _players,
                    online,
                    DamageSource.MeleeFrom(attacker.RuntimeId),
                    AttackDamage,
                    clock.CurrentTick,
                    dx, dz);
            }
        }
    }
}
