using Zenith.Gameplay.Ports;
using Zenith.Gameplay.Survival;
using Zenith.Packets;
using Zenith.Session;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>Who exists: the player list, adding a player actor, removing any actor.</summary>
sealed partial class EntityProtocol
{
    public void SendPlayerListAdd(
        Guid uuid,
        long actorUniqueId,
        string username,
        SerializedSkin? skin = null,
        byte[]? skinRgba = null,
        uint skinWidth = 0,
        uint skinHeight = 0,
        bool verified = false,
        string xboxUserId = "",
        string platformChatId = "",
        int buildPlatform = -1)
    {
        _session.SendDataPacket(new PlayerListPacket
        {
            Type = PlayerListPacket.TypeAdd,
            Entries =
            [
                PlayerListEntry.ForAdd(
                    uuid,
                    actorUniqueId,
                    username,
                    skin,
                    skinRgba,
                    skinWidth,
                    skinHeight,
                    verified,
                    xboxUserId,
                    platformChatId,
                    buildPlatform)
            ]
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
        int gameMode = AbilityBits.WireGameModeSurvival,
        bool sneaking = false,
        bool sprinting = false,
        string platformChatId = "",
        string deviceId = "",
        int buildPlatform = -1,
        bool usingItem = false)
    {
        _session.SendDataPacket(new AddPlayerPacket
        {
            Uuid = uuid,
            Username = username,
            ActorRuntimeId = actorRuntimeId,
            PlatformChatId = platformChatId,
            PositionX = x,
            PositionY = y,
            PositionZ = z,
            Pitch = pitch,
            Yaw = yaw,
            HeadYaw = headYaw,
            HeldItem = heldItem,
            GameMode = gameMode,
            Sneaking = sneaking,
            Sprinting = sprinting,
            UsingItem = usingItem,
            DeviceId = deviceId,
            BuildPlatform = buildPlatform
        });
    }

    public void SendRemoveActor(long actorUniqueId)
    {
        _session.SendDataPacket(new RemoveActorPacket { ActorUniqueId = actorUniqueId });
    }
}
