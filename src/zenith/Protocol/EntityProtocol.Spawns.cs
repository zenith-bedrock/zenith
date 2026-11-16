using Zenith.Gameplay.Ports;
using Zenith.Gameplay.Survival;
using Zenith.Packets;
using Zenith.Session;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>Actors that are not players: falling blocks, dropped items, projectiles, mounts, and mobs.</summary>
sealed partial class EntityProtocol
{
    /// <summary>Falling-block actor (ADR §95). <paramref name="y"/> is block-anchored, no eye offset.</summary>
    public void SendAddFallingBlock(long entityUniqueId, ulong entityRuntimeId, int blockRuntimeId, float x, float y, float z)
    {
        _session.SendDataPacket(new AddActorPacket
        {
            EntityUniqueId = entityUniqueId,
            EntityRuntimeId = entityRuntimeId,
            EntityType = "minecraft:falling_block",
            PositionX = x,
            PositionY = y,
            PositionZ = z,
            Variant = blockRuntimeId
        });
    }

    /// <summary>Experience orb actor (plan 003): <c>minecraft:xp_orb</c> with its value in metadata. Position is the orb's feet.</summary>
    public void SendAddExperienceOrb(long entityId, int points, float x, float y, float z, float velocityX, float velocityY, float velocityZ)
    {
        _session.SendDataPacket(new AddActorPacket
        {
            EntityUniqueId = entityId,
            EntityRuntimeId = (ulong)entityId,
            EntityType = "minecraft:xp_orb",
            PositionX = x,
            PositionY = y,
            PositionZ = z,
            VelocityX = velocityX,
            VelocityY = velocityY,
            VelocityZ = velocityZ,
            ExperiencePoints = points
        });
    }

    /// <summary>
    /// Dropped item entity at cell center (ADR §26 wire). Velocity is server-decided once at spawn
    /// (a toss, e.g. Q-drop) — Zenith does not run per-tick floor-drop physics, so the client's own
    /// local simulation owns the toss arc from here; a merged/republished cell always passes zero
    /// (it's already settled, not a fresh toss).
    /// </summary>
    public void SendAddItemActor(
        long entityRuntimeId,
        NetworkItemStack item,
        float x,
        float y,
        float z,
        float velocityX = 0f,
        float velocityY = 0f,
        float velocityZ = 0f)
    {
        _session.SendDataPacket(new AddItemActorPacket
        {
            EntityUniqueId = entityRuntimeId,
            EntityRuntimeId = (ulong)entityRuntimeId,
            Item = item,
            PositionX = x,
            PositionY = y,
            PositionZ = z,
            VelocityX = velocityX,
            VelocityY = velocityY,
            VelocityZ = velocityZ,
            FromFishing = false
        });
    }

    /// <summary>Mount/dismount actor link (Phase XX). </summary>
    public void SendSetActorLink(long riderUniqueId, long riddenUniqueId, ActorLinkKind link, bool immediate = true, bool causedByRider = true)
    {
        _session.SendDataPacket(new SetActorLinkPacket
        {
            RiderUniqueId = riderUniqueId,
            RiddenUniqueId = riddenUniqueId,
            LinkType = link switch
            {
                ActorLinkKind.Rider => SetActorLinkPacket.TypeRider,
                _ => SetActorLinkPacket.TypeRemove,
            },
            Immediate = immediate,
            CausedByRider = causedByRider
        });
    }

    /// <summary>Short-lived snowball projection for the concrete ProjectileSystem slice.</summary>
    public void SendAddProjectile(
        long entityUniqueId, ulong entityRuntimeId,
        float x, float y, float z, float velocityX, float velocityY, float velocityZ)
    {
        _session.SendDataPacket(new AddActorPacket
        {
            EntityUniqueId = entityUniqueId,
            EntityRuntimeId = entityRuntimeId,
            EntityType = "minecraft:snowball",
            PositionX = x,
            PositionY = y,
            PositionZ = z,
            VelocityX = velocityX,
            VelocityY = velocityY,
            VelocityZ = velocityZ
        });
    }

    /// <summary>
    /// Adapts an authoritative floor-drop stack to the Bedrock item representation before
    /// transmitting its actor. Gameplay owns whether a drop exists; wire conversion stays here.
    /// </summary>
    public void SendFloorDropActor(
        long entityRuntimeId, StackId stackId, int count, float x, float y, float z,
        float velocityX = 0f, float velocityY = 0f, float velocityZ = 0f)
    {
        var item = _session.Protocol.Inventory.DescribeStack(stackId, count);
        if (item.NetworkId == 0) return; // invalid/air item crashes Bedrock near player
        SendAddItemActor(entityRuntimeId, item, x, y, z, velocityX, velocityY, velocityZ);
    }

    public void SendTakeItemActor(ulong itemEntityRuntimeId, ulong takerEntityRuntimeId)
    {
        _session.SendDataPacket(new TakeItemActorPacket
        {
            ItemEntityRuntimeId = itemEntityRuntimeId,
            TakerEntityRuntimeId = takerEntityRuntimeId
        });
    }

    /// <summary>How a mob kind is introduced on the wire: its actor type, and which metadata block it needs.</summary>
    private readonly record struct MobWire(string Type, (float Width, float Height)? Dimensions = null, bool Zombie = false, bool Bat = false);

    private static readonly Dictionary<MobKind, MobWire> MobWires = new()
    {
        [MobKind.Zombie] = new("minecraft:zombie", Zombie: true),
        [MobKind.Skeleton] = new("minecraft:skeleton", Zenith.Gameplay.Entities.MobDefinitions.Skeleton.Dimensions),
        [MobKind.Cow] = new("minecraft:cow", Zenith.Gameplay.Entities.MobDefinitions.Cow.Dimensions),
        [MobKind.Creeper] = new("minecraft:creeper", Zenith.Gameplay.Entities.MobDefinitions.Creeper.Dimensions),
        [MobKind.Enderman] = new("minecraft:enderman", Zenith.Gameplay.Entities.MobDefinitions.Enderman.Dimensions),
        [MobKind.Bat] = new("minecraft:bat", Bat: true),
        [MobKind.Spider] = new("minecraft:spider", Zenith.Gameplay.Entities.MobDefinitions.Spider.Dimensions),
        [MobKind.Villager] = new("minecraft:villager", Zenith.Gameplay.Entities.MobDefinitions.Villager.Dimensions),
        [MobKind.Golem] = new("minecraft:iron_golem", Zenith.Gameplay.Entities.MobDefinitions.Golem.Dimensions),
        [MobKind.Minecart] = new("minecraft:minecart"),
        [MobKind.Fish] = new("minecraft:cod", Zenith.Gameplay.Entities.MobDefinitions.Fish.Dimensions),
    };

    /// <summary>Introduces a mob to this client. Spawn visuals only: behaviour lives in the species' system.</summary>
    public void SendAddMob(MobKind kind, long entityUniqueId, ulong entityRuntimeId, float x, float y, float z, float yaw)
    {
        if (!MobWires.TryGetValue(kind, out var wire))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "No wire actor type for this mob kind.");

        _session.SendDataPacket(new AddActorPacket
        {
            EntityUniqueId = entityUniqueId,
            EntityRuntimeId = entityRuntimeId,
            EntityType = wire.Type,
            PositionX = x,
            PositionY = y,
            PositionZ = z,
            Yaw = yaw,
            HeadYaw = yaw,
            BodyYaw = yaw,
            ZombieMetadata = wire.Zombie,
            BatMetadata = wire.Bat,
            Dimensions = wire.Dimensions
        });
    }
}
