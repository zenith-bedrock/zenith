using Zenith.Ecs;
using Zenith.Gameplay.Ports;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Survival;

namespace Zenith.Gameplay.Entities;

/// <summary>What a mob's effects do to a hit before its species sees it (the mob counterpart of the Resistance and Fire Resistance stages of the player pipeline).</summary>
static class MobMitigation
{
    public static float Apply(EntityRuntime stores, EntityId id, DamageSource source, float amount)
    {
        if (!stores.Bodies.TryGet(id, out var component) || component.Body.Effects.Active.Count == 0) return amount;
        if (source.Cause.IsFire() && EffectRules.ImmuneToFire(component.Body)) return 0f;
        return EffectRules.AfterResistance(component.Body, source, amount);
    }
}

/// <summary>
/// Status effects on mobs: the same table, the same <see cref="Body"/> and the same stacking rule as a player's, ticked here for every mob that
/// has been given an effect. Poison, wither, regeneration and the instant kinds act through the species' own damage path
/// (<see cref="DamageDispatch"/>), so a death keeps its loot and consequences; Health Boost and Absorption move the mob's maximum and shield
/// because its <see cref="HealthState"/> is the shared one. Sole writer of mob effects; clients see them as a MobEffect on the mob.
/// </summary>
[GameSystem("mob-effect")]
sealed class MobEffectSystem(EntityRuntime stores, DamageDispatch damage) : IGameSystem
{
    private readonly List<EntityId> _scratch = [];
    private readonly List<EffectType> _expired = [];

    /// <summary>Gives an effect to a mob now (an instant kind acts at once; the others last <paramref name="durationTicks"/>). False when the mob has no health or a stronger one runs.</summary>
    public bool Give(EntityId id, EffectType type, int amplifier, int durationTicks, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (!stores.TryGetBody(id, out var body) || body.Health.IsDead) return false;
        amplifier = Math.Clamp(amplifier, 0, 3);
        var definition = EffectTable.Default.Of(type);
        if (definition.Instant is { } instant)
        {
            if (instant.Heals) body.Health.Heal(instant.InstantAmount(amplifier));
            else damage.TryApplyDamage(id, new DamageSource(instant.Cause), instant.InstantAmount(amplifier), online, clock.CurrentTick);
            return true;
        }

        var duration = Math.Max(1, durationTicks);
        var wasActive = body.Effects.Active.ContainsKey(type);
        if (!body.Give(type, amplifier, clock.CurrentTick + (ulong)duration, clock.CurrentTick)) return false;
        Tell(id, wasActive ? EffectChange.Update : EffectChange.Add, type, amplifier, duration, online);
        return true;
    }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        _scratch.Clear();
        foreach (var id in Query.With(stores.Bodies, stores.Health)) _scratch.Add(id);

        foreach (var id in _scratch)
        {
            if (!stores.Bodies.TryGet(id, out var component) || component.Body.Effects.Active.Count == 0) continue;
            var body = component.Body;
            if (body.Health.IsDead) continue;

            _expired.Clear();
            foreach (var (type, effect) in body.Effects.Active)
            {
                if (effect.HasExpired(clock.CurrentTick))
                {
                    _expired.Add(type);
                    continue;
                }

                if (EffectTable.Default.Of(type).Periodic is { } periodic && periodic.DueAt(clock.CurrentTick, effect.Amplifier))
                    Run(id, body, periodic, clock, online);
            }

            foreach (var type in _expired)
            {
                body.Remove(type);
                Tell(id, EffectChange.Remove, type, 0, 0, online);
            }
        }
    }

    private void Run(EntityId id, Body body, EffectAction action, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (action.Heals)
        {
            body.Health.Heal(action.Amount);
            return;
        }

        var amount = action.DamageFor(body.Health.Current);
        if (amount > 0f) damage.TryApplyDamage(id, new DamageSource(action.Cause), amount, online, clock.CurrentTick);
    }

    private void Tell(EntityId id, EffectChange change, EffectType type, int amplifier, int durationTicks, IReadOnlyList<Player.Player> online)
    {
        if (!stores.Identities.TryGet(id, out var identity)) return;
        foreach (var peer in online)
            if (peer.IsInGame) peer.Client.Entity.SendMobEffect(identity.ActorRuntimeId, change, (int)type, amplifier, showParticles: true, durationTicks);
    }
}
