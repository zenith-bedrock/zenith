using Zenith.Gameplay.Ports;
using Zenith.Gameplay.Survival;
using Zenith.Packets;
using Zenith.Session;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>The local player's own life cycle: death screen, respawn handshake, abilities and game type.</summary>
sealed partial class EntityProtocol
{
    /// <summary>Death screen cause (DeathInfo 0xbd).</summary>
    public void SendDeathInfo(string cause, params string[] messages)
    {
        _session.SendDataPacket(new DeathInfoPacket
        {
            Cause = cause,
            Messages = messages
        });
    }

    /// <summary>Respawn handshake state (0x2d). Position is eye-space like StartGame.</summary>
    public void SendRespawn(float eyeX, float eyeY, float eyeZ, byte state, ulong entityRuntimeId)
    {
        _session.SendDataPacket(new RespawnPacket
        {
            PositionX = eyeX,
            PositionY = eyeY,
            PositionZ = eyeZ,
            State = state,
            EntityRuntimeId = entityRuntimeId
        });
    }

    /// <summary>Server → client: searching for spawn (Gameplay must not reference Packets constants).</summary>
    public void SendRespawnSearching(float eyeX, float eyeY, float eyeZ, ulong entityRuntimeId) =>
        SendRespawn(eyeX, eyeY, eyeZ, RespawnPacket.StateSearchingForSpawn, entityRuntimeId);

    /// <summary>Server → client: ready to spawn at position.</summary>
    public void SendRespawnReady(float eyeX, float eyeY, float eyeZ, ulong entityRuntimeId) =>
        SendRespawn(eyeX, eyeY, eyeZ, RespawnPacket.StateReadyToSpawn, entityRuntimeId);

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

    /// <summary>Runtime SetPlayerGameType after GameLoop applies mode (§52).</summary>
    public void SendPlayerGameType(int wireGameMode) =>
        _session.SendDataPacket(new SetPlayerGameTypePacket { GameType = wireGameMode });
}
