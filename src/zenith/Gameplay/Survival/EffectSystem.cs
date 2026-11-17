using Zenith.Gameplay.Permissions;
using Zenith.Gameplay.Ports;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Runtime;
using Zenith.Player;

namespace Zenith.Gameplay.Survival;

/// <summary>
/// Player effects: apply/refresh/clear via <see cref="EffectIntent"/>, the periodic and instant rules of <see cref="EffectTable"/>
/// (poison, wither, regeneration, instant health and damage), natural expiry. Owns <c>Player.Effects.Active</c> (held by the player's
/// <see cref="Body"/>, which turns effects into attribute modifiers: Health Boost, Absorption and the rest). What each effect does is data in
/// <c>data/effects.json</c>; the mob counterpart is <c>MobEffectSystem</c>.
/// </summary>
[GameSystem("effect")]
sealed class EffectSystem : IGameSystem
{
    private readonly PlayerManager _players;
    private readonly List<EffectType> _expiredScratch = new();

    private readonly AuthorityGate _gate;

    public EffectSystem(PlayerManager players, AuthorityGate gate)
    {
        _players = players;
        _gate = gate;
    }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        foreach (var player in online)
        {
            if (!player.IsInGame) continue;

            ApplyPendingIntent(player, clock);
            if (player.IsDead || player.Effects.Active.Count == 0) continue;

            TickActiveEffects(player, clock, online);
        }
    }

    private void ApplyPendingIntent(Player.Player player, GameClock clock)
    {
        if (!player.Effects.TryConsumeEffectIntent(out var intent)) return;
        if (!_gate.Confirms(intent.Issuer)) return; // revoked, or the issuer left, since the command was queued

        var entity = player.Client.Entity;
        var rid = (ulong)player.RuntimeId;

        if (intent.RemoveOne)
        {
            if (!player.Effects.RemoveEffect(intent.Type)) return;
            entity.SendMobEffect(rid, EffectChange.Remove, (int)intent.Type, 0, false, 0);
            ShowBody(player);
            return;
        }

        if (intent.ClearAll)
        {
            var removed = player.Effects.Active.Keys.ToArray();
            if (!player.Effects.ClearEffects()) return;
            foreach (var type in removed)
                entity.SendMobEffect(rid, EffectChange.Remove, (int)type, 0, false, 0);
            ShowBody(player);
            return;
        }

        var amplifier = Math.Clamp(intent.Amplifier, 0, 3);
        if (EffectTable.Default.Of(intent.Type).Instant is { } instant)
        {
            ApplyInstant(player, instant, amplifier, clock);
            return;
        }

        var durationTicks = Math.Max(1, intent.DurationTicks);
        var expiresAtTick = clock.CurrentTick + (ulong)durationTicks;
        var wasActive = player.Effects.Active.ContainsKey(intent.Type);
        if (!player.Effects.ApplyOrRefreshEffect(intent.Type, amplifier, expiresAtTick, clock.CurrentTick))
            return; // a stronger or longer instance is already active - vanilla ignores the weaker one.

        entity.SendMobEffect(
            rid,
            wasActive ? EffectChange.Update : EffectChange.Add,
            (int)intent.Type,
            amplifier,
            showParticles: true,
            durationTicks);
        ShowBody(player);
    }

    /// <summary>The maximum health and the absorption an effect changed are attributes: the client's hearts follow them.</summary>
    private void ShowBody(Player.Player player)
    {
        player.Client.Entity.SendPlayerAttributes(player);
        PlayerVisibility.RelayHealth(player, _players.Online);
    }

    private void TickActiveEffects(Player.Player player, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        var expired = _expiredScratch;
        expired.Clear();

        foreach (var (type, effect) in player.Effects.Active)
        {
            if (effect.HasExpired(clock.CurrentTick))
            {
                expired.Add(type);
                continue;
            }

            if (EffectTable.Default.Of(type).Periodic is { } periodic && periodic.DueAt(clock.CurrentTick, effect.Amplifier))
                RunPeriodic(player, periodic, clock, online);
        }

        if (expired.Count == 0) return;
        var entity = player.Client.Entity;
        var rid = (ulong)player.RuntimeId;
        foreach (var type in expired)
        {
            player.Effects.RemoveEffect(type);
            entity.SendMobEffect(rid, EffectChange.Remove, (int)type, 0, false, 0);
        }

        ShowBody(player);
    }

    /// <summary>
    /// One tick of a periodic rule. Healing stops at the maximum; damage is magic or wither (around armor), never takes poison below 1 and
    /// always does its table amount per due tick: a higher amplifier ticks more often, it does not deal more damage per tick.
    /// </summary>
    private void RunPeriodic(Player.Player player, EffectAction action, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (action.Heals)
        {
            if (player.Health >= player.MaxHealth) return;
            player.Heal(action.Amount);
            player.Client.Entity.SendPlayerAttributes(player);
            PlayerVisibility.RelayHealth(player, online);
            return;
        }

        var amount = action.DamageFor(player.Health);
        if (amount <= 0f) return;
        PlayerDamage.ApplyCore(player, _players, new DamageSource(action.Cause), amount, clock.CurrentTick).Conclude(online);
    }

    /// <summary>Instant Health mends 4 a level (doubling each level); Instant Damage hurts 6 a level. Nothing stays active.</summary>
    private void ApplyInstant(Player.Player player, EffectAction action, int amplifier, GameClock clock)
    {
        var amount = action.InstantAmount(amplifier);
        if (action.Heals)
        {
            player.Heal(amount);
            player.Client.Entity.SendPlayerAttributes(player);
            PlayerVisibility.RelayHealth(player, _players.Online);
            return;
        }

        PlayerDamage.ApplyCore(player, _players, new DamageSource(action.Cause), amount, clock.CurrentTick).Conclude(_players.Online);
    }
}
