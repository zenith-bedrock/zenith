namespace Zenith.Ecs;

/// <summary>
/// Phase XXI addendum — the cohesive ECS access boundary: one object every migrated system takes
/// as a single constructor parameter, instead of the allocator plus one <see cref="ComponentStore{T}"/>
/// per shared component type (five-plus raw dependencies was flagged as the ECS's own version of
/// the DX problem it was meant to fix). Groups exactly the components proven shared across
/// multiple migrated actors (see docs/history/phases/phase-xxi-ecs-foundation-findings.md, "Initial component
/// model") — feature-specific stores (<c>ZombieState</c>, <c>VehicleOccupancy</c>,
/// <c>ProjectileState</c>) stay owned by their one system, not added here, because adding every
/// feature component to this shared object would just relocate the same proliferation.
///
/// Renamed from <c>EntityRuntime</c> (Phase XXI addendum 2): once it grew
/// <see cref="CreateActor"/>/<see cref="DestroyActor"/> and a debug <see cref="Describe"/>, calling
/// it a "stores" bag undersold what it actually is — the runtime gameplay code touches for
/// everything entity-shaped, not just component storage. <c>World</c> was avoided since Zenith
/// already has a gameplay <see cref="global::Zenith.World.World"/> with an unrelated meaning.
/// </summary>
sealed class EntityRuntime
{
    public EntityWorld Entities { get; } = new();
    public RuntimeIdIndex RuntimeIds { get; } = new();
    public ComponentStore<Position> Positions { get; }
    public ComponentStore<Velocity> Velocities { get; }
    public ComponentStore<HealthComponent> Health { get; }
    public ComponentStore<ActorIdentity> Identities { get; }
    public ComponentStore<DespawnTracking> Despawn { get; }

    public EntityRuntime()
    {
        Positions = new ComponentStore<Position>(Entities);
        Velocities = new ComponentStore<Velocity>(Entities);
        Health = new ComponentStore<HealthComponent>(Entities);
        Identities = new ComponentStore<ActorIdentity>(Entities);
        Despawn = new ComponentStore<DespawnTracking>(Entities);
    }

    /// <summary>
    /// Bundles the three steps every migrated actor's spawn repeats: allocate the
    /// <see cref="EntityId"/>, attach <see cref="Position"/> and <see cref="ActorIdentity"/>
    /// (every replicated actor needs both), and register the Bedrock runtime-id mapping. Callers
    /// still <c>Set</c> their own <see cref="HealthComponent"/>/<see cref="Velocity"/>/feature
    /// components afterward — this only removes the part that was identical everywhere.
    ///
    /// Transactional (Phase XXI addendum 2): if <see cref="RuntimeIdIndex.Register"/> rejects a
    /// duplicate <paramref name="actorRuntimeId"/> (which <c>PlayerManager.AllocateRuntimeId</c>
    /// should never actually produce, but the invariant is proven regardless — see
    /// <c>EntityRuntimeTests</c>), the just-created entity is destroyed before returning
    /// <see langword="null"/>. Entity creation either commits completely — Position, Identity,
    /// and the runtime-id mapping all present — or leaves nothing behind.
    /// </summary>
    public EntityId? CreateActor(long actorUniqueId, ulong actorRuntimeId, float x, float y, float z, float yaw = 0f)
    {
        var id = Entities.Create();
        Positions.Set(id, new Position { X = x, Y = y, Z = z, Yaw = yaw });
        Identities.Set(id, new ActorIdentity { ActorUniqueId = actorUniqueId, ActorRuntimeId = actorRuntimeId });
        if (RuntimeIds.Register(actorRuntimeId, id))
            return id;

        Entities.Destroy(id); // Rollback: no partially-created entity survives a rejected runtime-id registration.
        return null;
    }

    /// <summary>
    /// The one authoritative teardown call: unregisters the runtime-id mapping and destroys the
    /// entity, which structurally removes every attached component (see
    /// <see cref="EntityWorld.Destroy"/>) — feature systems never need to remember which stores
    /// they used. Feature-specific consequences (loot, XP, dismounting an occupant, sending
    /// <c>RemoveActor</c> to viewers) must happen BEFORE this call; once called, the entity's data
    /// is gone.
    /// </summary>
    public void DestroyActor(ulong actorRuntimeId, EntityId id)
    {
        RuntimeIds.Unregister(actorRuntimeId);
        Entities.Destroy(id);
    }

    /// <summary>
    /// Debug/diagnostics only — never called from hot gameplay paths. Answers "what do we know
    /// about this entity" without reflection: alive state plus presence in each shared store this
    /// object owns. Feature-specific component presence isn't visible here since this type has no
    /// reference to per-system stores; a system-level debug helper can extend this if ever needed.
    /// </summary>
    internal string Describe(EntityId id)
    {
        if (!Entities.IsAlive(id)) return $"{id} (not alive)";
        var parts = new List<string>();
        if (Positions.Has(id)) parts.Add(nameof(Position));
        if (Velocities.Has(id)) parts.Add(nameof(Velocity));
        if (Health.Has(id)) parts.Add(nameof(HealthComponent));
        if (Identities.Has(id)) parts.Add(nameof(ActorIdentity));
        if (Despawn.Has(id)) parts.Add(nameof(DespawnTracking));
        return $"{id} alive, components=[{string.Join(", ", parts)}]";
    }
}
