using Zenith.Gameplay.Runtime;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Session;

namespace Zenith.Gameplay.Survival;

/// <summary>
/// The first concrete effect slice (Phase XI.3): apply/refresh/clear via <see cref="EffectIntent"/>,
/// periodic Poison damage and Regeneration healing, natural expiry. Owns <c>Player.Effects</c>; no
/// EffectFramework, ModifierSystem or generic gameplay-component pipeline — each effect type is one
/// concrete branch below.
/// </summary>
sealed class EffectSystem : IGameSystem
{
    /// <summary>
    /// Vanilla-parity cadence at amplifier 0. Vanilla halves the tick interval per amplifier level
    /// (<c>interval &gt;&gt; amplifier</c>) rather than scaling the per-tick amount — see
    /// <see cref="TickPoison"/>/<see cref="TickRegeneration"/>.
    /// </summary>
    private const ulong PoisonIntervalTicks = 25;
    private const ulong RegenIntervalTicks = 50;

    private readonly PlayerManager _players;
    private readonly List<EffectType> _expiredScratch = new();

    public EffectSystem(PlayerManager players) => _players = players;

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        foreach (var player in online)
        {
            if (!player.IsInGame) continue;

            ApplyPendingIntent(player, clock);
            if (player.IsDead || player.Effects.Count == 0) continue;

            TickActiveEffects(player, clock, online);
        }
    }

    private static void ApplyPendingIntent(Player.Player player, GameClock clock)
    {
        if (!player.TryConsumeEffectIntent(out var intent)) return;

        var entity = player.Session.Protocol.Entity;
        var rid = (ulong)player.RuntimeId;

        if (intent.ClearAll)
        {
            if (!player.ClearEffects()) return;
            // Best-effort per-type Remove is unnecessary here — the client only ever learned about
            // types this player actually had, and Effects was just fully drained.
            return;
        }

        var amplifier = Math.Clamp(intent.Amplifier, 0, 3);
        var durationTicks = Math.Max(1, intent.DurationTicks);
        var expiresAtTick = clock.CurrentTick + (ulong)durationTicks;
        var wasActive = player.Effects.ContainsKey(intent.Type);
        if (!player.ApplyOrRefreshEffect(intent.Type, amplifier, expiresAtTick, clock.CurrentTick))
            return; // a stronger or longer instance is already active — vanilla ignores the weaker one.

        entity.SendMobEffect(
            rid,
            wasActive ? MobEffectPacket.EventUpdate : MobEffectPacket.EventAdd,
            (int)intent.Type,
            amplifier,
            showParticles: true,
            durationTicks);
    }

    private void TickActiveEffects(Player.Player player, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        var expired = _expiredScratch;
        expired.Clear();

        foreach (var (type, effect) in player.Effects)
        {
            if (effect.HasExpired(clock.CurrentTick))
            {
                expired.Add(type);
                continue;
            }

            switch (type)
            {
                case EffectType.Poison:
                    TickPoison(player, effect, clock, online);
                    break;
                case EffectType.Regeneration:
                    TickRegeneration(player, effect, clock, online);
                    break;
            }
        }

        if (expired.Count == 0) return;
        var entity = player.Session.Protocol.Entity;
        var rid = (ulong)player.RuntimeId;
        foreach (var type in expired)
        {
            player.RemoveEffect(type);
            entity.SendMobEffect(rid, MobEffectPacket.EventRemove, (int)type, 0, false, 0);
        }
    }

    /// <summary>
    /// Poison never reduces health below 1 and bypasses armor (vanilla parity, magic damage). Amount
    /// per tick is always 1 — a higher amplifier ticks more often (<see cref="PoisonIntervalTicks"/>),
    /// it does not deal more damage per tick.
    /// </summary>
    private void TickPoison(Player.Player player, ActiveEffect effect, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (clock.CurrentTick % (PoisonIntervalTicks >> effect.Amplifier) != 0) return;
        if (player.Health <= 1f) return;

        var amount = MathF.Min(1f, player.Health - 1f);
        PlayerDamage.ApplyCore(player, _players, DamageSource.Magic, amount, clock.CurrentTick).Conclude(online);
    }

    /// <summary>Amount per tick is always 1 — a higher amplifier ticks more often, matching Poison.</summary>
    private static void TickRegeneration(Player.Player player, ActiveEffect effect, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (clock.CurrentTick % (RegenIntervalTicks >> effect.Amplifier) != 0) return;
        if (player.Health >= player.MaxHealth) return;

        player.Heal(1f);
        player.Session.Protocol.Entity.SendPlayerAttributes(player);
        PlayerVisibility.RelayHealth(player, online);
    }
}
