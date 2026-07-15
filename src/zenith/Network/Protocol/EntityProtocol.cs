using Zenith.Network.Packets;
using Zenith.Network.Session;

namespace Zenith.Network.Protocol;

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
        _session.SendDataPacket(new MoveActorAbsolutePacket
        {
            ActorRuntimeId = actorRuntimeId,
            Flags = flags,
            PositionX = x,
            PositionY = y,
            PositionZ = z,
            Pitch = pitch,
            Yaw = yaw,
            HeadYaw = headYaw
        });
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
        int gameMode = 0)
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

    /// <summary>Frozen health/hunger/… defaults — not domain authority (§34).</summary>
    public void SendDefaultAttributes(ulong actorRuntimeId)
    {
        _session.SendDataPacket(UpdateAttributesPacket.CreateFrozenDefaults(actorRuntimeId));
    }
}
