using Zenith.Network.Packets;
using Zenith.Network.Session;

namespace Zenith.Network.Protocol;

/// <summary>Pose for batched MoveActorAbsolute (Gameplay fills; Protocol serializes).</summary>
readonly struct AbsoluteActorPose
{
    public ulong ActorRuntimeId { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public float Pitch { get; init; }
    public float Yaw { get; init; }
    public float HeadYaw { get; init; }
}

/// <summary>Transmite intenções de entidade. Sem lógica de gameplay.</summary>
sealed class EntityProtocol
{
    private readonly NetworkSession _session;

    public EntityProtocol(NetworkSession session) => _session = session;

    public void SendMoveAbsolute(
        ulong actorRuntimeId,
        float x,
        float y,
        float z,
        float pitch,
        float yaw,
        float headYaw,
        byte flags = 0)
    {
        _session.SendDataPacket(CreateMoveAbsolute(actorRuntimeId, x, y, z, pitch, yaw, headYaw, flags));
    }

    /// <summary>One GamePacket envelope for N Absolute moves (ADR §44).</summary>
    public void SendMoveAbsolutes(IReadOnlyList<AbsoluteActorPose> poses)
    {
        if (poses.Count == 0) return;
        if (poses.Count == 1)
        {
            var p = poses[0];
            SendMoveAbsolute(p.ActorRuntimeId, p.X, p.Y, p.Z, p.Pitch, p.Yaw, p.HeadYaw);
            return;
        }

        var packets = new DataPacket[poses.Count];
        for (var i = 0; i < poses.Count; i++)
        {
            var p = poses[i];
            packets[i] = CreateMoveAbsolute(p.ActorRuntimeId, p.X, p.Y, p.Z, p.Pitch, p.Yaw, p.HeadYaw);
        }

        _session.SendDataPacket(packets);
    }

    private static MoveActorAbsolutePacket CreateMoveAbsolute(
        ulong actorRuntimeId,
        float x,
        float y,
        float z,
        float pitch,
        float yaw,
        float headYaw,
        byte flags = 0) =>
        new()
        {
            ActorRuntimeId = actorRuntimeId,
            Flags = flags,
            PositionX = x,
            PositionY = y,
            PositionZ = z,
            Pitch = pitch,
            Yaw = yaw,
            HeadYaw = headYaw
        };

    /// <summary>Local camera snap — MovePlayer Teleport (ADR §41). Not for peers.</summary>
    public void SendMovePlayerTeleport(
        ulong entityRuntimeId,
        float x,
        float y,
        float z,
        float pitch,
        float yaw,
        float headYaw,
        ulong tick = 0)
    {
        _session.SendDataPacket(MovePlayerPacket.CreateTeleport(
            entityRuntimeId, x, y, z, pitch, yaw, headYaw, tick));
    }

    public void SendPlayerListAdd(Guid uuid, long actorUniqueId, string username, byte[]? skinRgba = null, uint skinWidth = 0, uint skinHeight = 0)
    {
        _session.SendDataPacket(new PlayerListPacket
        {
            Type = PlayerListPacket.TypeAdd,
            Entries = [PlayerListEntry.ForAdd(uuid, actorUniqueId, username, skinRgba, skinWidth, skinHeight)]
        });
    }

    public void SendPlayerListRemove(Guid uuid)
    {
        _session.SendDataPacket(new PlayerListPacket
        {
            Type = PlayerListPacket.TypeRemove,
            Entries = [PlayerListEntry.ForRemove(uuid)]
        });
    }

    public void SendAddPlayer(
        Guid uuid,
        string username,
        ulong actorRuntimeId,
        float x,
        float y,
        float z,
        float pitch,
        float yaw,
        float headYaw,
        NetworkItemStack heldItem,
        int gameMode = AbilityBits.WireGameModeSurvival)
    {
        _session.SendDataPacket(new AddPlayerPacket
        {
            Uuid = uuid,
            Username = username,
            ActorRuntimeId = actorRuntimeId,
            PositionX = x,
            PositionY = y,
            PositionZ = z,
            Pitch = pitch,
            Yaw = yaw,
            HeadYaw = headYaw,
            HeldItem = heldItem,
            GameMode = gameMode
        });
    }

    public void SendMobEquipment(ulong actorRuntimeId, NetworkItemStack item, int hotbarSlot)
    {
        _session.SendDataPacket(new MobEquipmentPacket
        {
            ActorRuntimeId = (long)actorRuntimeId,
            Item = item,
            InventorySlot = hotbarSlot,
            HotbarSlot = hotbarSlot,
            WindowId = MobEquipmentPacket.WindowInventory
        });
    }

    public void SendRemoveActor(long actorUniqueId)
    {
        _session.SendDataPacket(new RemoveActorPacket { ActorUniqueId = actorUniqueId });
    }

    /// <summary>Local-player metadata seed (Breathing) — HUD honesty at spawn (§34).</summary>
    public void SendLocalActorData(ulong actorRuntimeId, string name)
    {
        _session.SendDataPacket(new SetActorDataPacket
        {
            ActorRuntimeId = actorRuntimeId,
            Name = name,
            Tick = 0
        });
    }

    /// <summary>Attributes from Player vitals (ADR §40) — remaining fields still seed constants.</summary>
    public void SendDefaultAttributes(ulong actorRuntimeId, float health, float hunger)
    {
        _session.SendDataPacket(UpdateAttributesPacket.CreateDefaults(actorRuntimeId, health, hunger));
    }

    /// <summary>Local UpdateAbilities seed / RequestAbility echo — runtime id = unique id (§37).</summary>
    public void SendLocalAbilities(long uniqueId, int wireGameMode, bool flying = true)
    {
        _session.SendDataPacket(UpdateAbilitiesPacket.Create(uniqueId, wireGameMode, flying));
    }

    /// <summary>LAN UpdateAdventureSettings defaults (§37).</summary>
    public void SendAdventureSettings()
    {
        _session.SendDataPacket(UpdateAdventureSettingsPacket.CreateLanDefaults());
    }
}
