using Zenith.Ecs;
using Zenith.Gameplay.Loot;
using Zenith.Gameplay.Survival;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// The part of an ECS actor species that is the same for all of them: composing a new actor (identity, health,
/// velocity, despawn tracking), the replication state, and the damage / loot / experience / destruction sequence.
/// A species' system owns one of these plus whatever is truly its own (its state component, AI, attacks).
/// </summary>
sealed class MobCore
{
    private readonly EntityRuntime _stores;
    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly LootTable _loot;
    private readonly Random _random;
    private readonly bool _withVelocity;

    public MobCore(
        MobDefinition definition, World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette,
        bool trackPoses = true, bool withVelocity = true, Random? random = null)
    {
        Definition = definition;
        ItemPalette = itemPalette;
        _world = world;
        _players = players;
        _stores = stores;
        _loot = LootTables.Get(definition.LootTable);
        _random = random ?? Random.Shared;
        _withVelocity = withVelocity;
        Replication = ActorReplication.ForMob(stores, definition.Kind, trackPoses);
    }

    public MobDefinition Definition { get; }
    public ItemPalette ItemPalette { get; }

    public ActorReplication Replication { get; }

    /// <summary>Creates the actor with the components every species has; the caller adds its own state component.</summary>
    public EntityId Spawn(float x, float y, float z)
    {
        var actorUniqueId = _players.AllocateRuntimeId();
        // CreateActor only returns null if AllocateRuntimeId ever produced a runtime id already in use, which it never
        // does by construction (EntityRuntimeTests prove the rollback path in isolation).
        var id = _stores.CreateActor(actorUniqueId, (ulong)actorUniqueId, x, y, z)
                 ?? throw new InvalidOperationException($"Duplicate actor runtime id allocated for a new {Definition.Name}.");
        _stores.Health.Set(id, new HealthComponent { State = new HealthState(Definition.MaxHealth) });
        if (_withVelocity) _stores.Velocities.Set(id, new Velocity()); // Y doubles as the gravity fall speed.
        _stores.Despawn.Set(id, new DespawnTracking());
        if (Definition.Melee is not null) _stores.MeleeCooldowns.Set(id, new MeleeCooldown());
        if (Definition.Knockback is { } knockback) _stores.Knockbacks.Set(id, new Knockback { DecayPerTick = knockback.DecayPerTick });
        return id;
    }

    /// <summary>
    /// Players attacking this actor: consumes at most one reach-checked attack intent and applies the damage through
    /// <paramref name="tryApplyDamage"/> (the species' own, so its death and knockback consequences run).
    /// </summary>
    public void ApplyPlayerAttacks(
        EntityId id, IReadOnlyList<Player.Player> online, ulong currentTick,
        Func<EntityId, DamageSource, float, IReadOnlyList<Player.Player>, ulong, bool> tryApplyDamage) =>
        DamageableActorCombat.ApplyPlayerMeleeAttacks(id, _stores, online, Definition, ItemPalette, currentTick, tryApplyDamage);

    /// <summary>
    /// Pushes the actor away from (<paramref name="fromX"/>, <paramref name="fromZ"/>) at its species' knockback strength; the motion
    /// itself is <see cref="KnockbackSystem"/>'s. Nothing for a species with no knockback profile or full resistance.
    /// </summary>
    public void ApplyKnockback(EntityId id, float fromX, float fromZ)
    {
        if (Definition.Knockback is not { } profile || profile.EffectiveStrength <= 0f) return;
        if (!_stores.Positions.TryGet(id, out var pos) || !_stores.Knockbacks.Has(id)) return;

        var dx = pos.X - fromX;
        var dz = pos.Z - fromZ;
        var lengthSquared = dx * dx + dz * dz;
        // Standing exactly on the source has no direction: push along +Z, as the zombie always did.
        var (nx, nz) = lengthSquared > 0.0001f ? (dx / MathF.Sqrt(lengthSquared), dz / MathF.Sqrt(lengthSquared)) : (0f, 1f);

        ref var push = ref _stores.Knockbacks.GetRef(id);
        push.X = nx * profile.EffectiveStrength;
        push.Z = nz * profile.EffectiveStrength;
    }

    /// <summary>True when the species' melee attack is off cooldown.</summary>
    public bool IsMeleeReady(EntityId id, ulong currentTick) =>
        _stores.MeleeCooldowns.TryGet(id, out var cooldown) && currentTick >= cooldown.NextAttackTick;

    /// <summary>Delays the next melee attack by <paramref name="ticks"/> (a species that spends its turn on something else, like a teleport).</summary>
    public void DelayMelee(EntityId id, ulong currentTick, int ticks)
    {
        if (_stores.MeleeCooldowns.Has(id)) _stores.MeleeCooldowns.GetRef(id).NextAttackTick = currentTick + (ulong)ticks;
    }

    /// <summary>
    /// The one melee attack on a player, for every species whose definition has one: off cooldown, target within the species' reach
    /// (horizontal), then damage with knockback away from the attacker, the cooldown, and the swing animation to every peer that sees
    /// the attacker. Returns true when the hit landed, so a species can add its own consequence (spider poison).
    /// </summary>
    public bool TryMeleeAttack(EntityId id, Player.Player target, ulong currentTick, IReadOnlyList<Player.Player> online)
    {
        if (Definition.Melee is not { } melee || !IsMeleeReady(id, currentTick)) return false;
        if (!_stores.Positions.TryGet(id, out var pos) || !_stores.Identities.TryGet(id, out var identity)) return false;
        var dx = target.PositionX - pos.X;
        var dz = target.PositionZ - pos.Z;
        if (dx * dx + dz * dz > melee.Reach * melee.Reach) return false;

        var result = PlayerDamage.ApplyCore(
            target, _players, DamageSource.MeleeFrom(identity.ActorUniqueId), melee.Damage, currentTick, dx, dz);
        result.Conclude(online);
        if (!result.Applied) return false;

        DelayMelee(id, currentTick, melee.CooldownTicks);
        foreach (var peer in online)
            if (Replication.Viewers.Contains((identity.ActorUniqueId, peer.RuntimeId)))
                peer.Client.Entity.SendAttackSwing(identity.ActorRuntimeId);
        return true;
    }

    /// <summary>
    /// Applies damage; on a kill, drops the loot, credits the experience, tells every peer who saw the actor and destroys it.
    /// <paramref name="onPeerRemoved"/> runs for each of those peers after the pose cache was cleared (a species that must send
    /// something more on death, like a minecart's rider link).
    /// </summary>
    public bool TryApplyDamage(
        EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick,
        Action<Player.Player>? onPeerRemoved = null)
    {
        // Read before combat destroys the actor: the pose cache is cleared by unique id even after the identity is gone.
        var hasIdentity = _stores.Identities.TryGet(id, out var identity);
        long? actorUniqueId = hasIdentity ? identity.ActorUniqueId : null;
        var applied = DamageableActorCombat.TryApplyDamage(
            id, _stores, source, amount, online, _world, _players, Replication.Viewers, _loot, _random, Definition.KillExperience, ItemPalette,
            Definition.Name, currentTick,
            destroyActor: killed =>
            {
                if (_stores.Identities.TryGet(killed, out var dying))
                    _stores.DestroyActor(dying.ActorRuntimeId, killed);
            },
            onDeathReplicatedToPeer: peer =>
            {
                if (actorUniqueId is { } unique) Replication.Forget(unique, peer);
                Replication.RemovalCount++;
                onPeerRemoved?.Invoke(peer);
            },
            Definition.DeathAnimationTicks,
            leaveCorpse: hasIdentity
                ? viewers => _stores.LeaveCorpse(identity, viewers, currentTick, Definition.DeathAnimationTicks)
                : null);

        // A hit that left the actor alive pushes it away from whoever dealt it (a player's melee, an arrow's shooter).
        if (applied && _stores.Entities.IsAlive(id) && source.OwnerRuntimeId is { } ownerId && _players.GetByRuntimeId(ownerId) is { } attacker)
            ApplyKnockback(id, attacker.PositionX, attacker.PositionZ);
        return applied;
    }
}
