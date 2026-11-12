using Zenith.Ecs;
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
    private readonly StackId _lootItem;
    private readonly bool _withVelocity;

    public MobCore(
        MobDefinition definition, World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette,
        bool trackPoses = true, bool withVelocity = true)
    {
        Definition = definition;
        _world = world;
        _players = players;
        _stores = stores;
        _lootItem = StackId.FromItem(itemPalette.Require(definition.LootItemName));
        _withVelocity = withVelocity;
        Replication = new ActorReplication(stores, trackPoses);
    }

    public MobDefinition Definition { get; }

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
        return id;
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
        long? actorUniqueId = _stores.Identities.TryGet(id, out var identity) ? identity.ActorUniqueId : null;
        return DamageableActorCombat.TryApplyDamage(
            id, _stores, source, amount, online, _world, _players, Replication.Viewers, _lootItem, Definition.KillExperience,
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
            });
    }
}
