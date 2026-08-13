using Zenith.Gameplay.Runtime;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Session;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// The first concrete effect slice (Phase XI.3): apply/refresh/clear via <see cref="EffectIntent"/>,
/// periodic Poison damage and Regeneration healing, natural expiry. Owns <c>Player.Effects</c>; no
/// EffectFramework, ModifierSystem or generic gameplay-component pipeline — each effect type is one
/// concrete branch below.
/// </summary>
sealed class EffectSystem : IGameSystem
{
    /// <summary>Vanilla-parity cadence: both effects tick once per second at amplifier 0.</summary>
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
        player.ApplyOrRefreshEffect(intent.Type, amplifier, expiresAtTick);

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

    /// <summary>Poison never reduces health below 1 and bypasses armor (vanilla parity, magic damage).</summary>
    private void TickPoison(Player.Player player, ActiveEffect effect, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (clock.CurrentTick % PoisonIntervalTicks != 0) return;
        if (player.Health <= 1f) return;

        var amount = MathF.Min(1f + effect.Amplifier, player.Health - 1f);
        _ = PlayerDamage.Apply(player, _players, online, DamageSource.Magic, amount);
    }

    private static void TickRegeneration(Player.Player player, ActiveEffect effect, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (clock.CurrentTick % RegenIntervalTicks != 0) return;
        if (player.Health >= player.MaxHealth) return;

        player.Heal(1f + effect.Amplifier);
        player.Session.Protocol.Entity.SendPlayerAttributes(player);
        PlayerVisibility.RelayHealth(player, online);
    }
}
