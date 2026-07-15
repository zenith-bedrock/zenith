using Zenith.Packets;
using Zenith.Raknet.Stream;
using Zenith.Server;
using Zenith.World;

namespace Zenith.Session.Handler;

/// <summary>
/// Negociação de resource packs. Ao receber STATUS_COMPLETED, manda StartGame via Protocol
/// e troca pra <see cref="PreSpawnSessionHandler"/>.
/// </summary>
class ResourcePacksSessionHandler : ISessionHandler
{
    public bool HandleDataPacket(NetworkSession session, DataPacket.HeaderInfo header, ref BinaryStream stream)
    {
        if (header.Id == (int)ProtocolInfo.CLIENT_CACHE_STATUS_PACKET) return true;

        if (header.Id != (int)ProtocolInfo.RESOURCE_PACK_CLIENT_RESPONSE_PACKET) return false;

        var response = DataPacket.From<ResourcePackClientResponsePacket>(ref stream);
        session.Context.Logger.Debug($"ResourcePackClientResponsePacket: {response.Status}");

        switch (response.Status)
        {
            case ResourcePackClientResponsePacket.STATUS_HAVE_ALL_PACKS:
                session.Protocol.ResourcePacks.SendStack(
                    mustAccept: false,
                    gameVersion: ServerIdentity.VersionName,
                    experimentsPreviouslyToggled: false,
                    hasEditorPacks: false);
                break;
            case ResourcePackClientResponsePacket.STATUS_COMPLETED:
                // PlayStatus(PLAYER_SPAWN) não é mandado aqui de propósito: PreSpawn manda
                // esse status só depois de publicar chunks.
                var player = session.Player!;
                // StartGame position = eyes (Vedrock spawn_y + player_eye_height).
                session.Protocol.World.SendStartGame(
                    levelName: session.Context.Config.World.Name,
                    entityRuntimeId: player.RuntimeId,
                    x: player.PositionX,
                    y: player.PositionY + Blocks.PlayerEyeHeight,
                    z: player.PositionZ,
                    pitch: player.Pitch,
                    yaw: player.Yaw,
                    spawnBlockX: 0,
                    spawnBlockY: Blocks.FlatSpawnY,
                    spawnBlockZ: 0,
                    useBlockNetworkIdHashes: true,
                    gameMode: (int)player.GameMode);
                session.Protocol.Inventory.SendItemRegistry();
                session.Protocol.Inventory.SendCreativeContent();
                session.Protocol.Inventory.SendCraftingData();
                // Empty BiomeDefinitionList — required once by modern clients (Vedrock/PNX parity).
                session.Protocol.World.SendEmptyBiomeDefinitionList();
                // Local HUD seed (§34): Breathing metadata + frozen attributes (before PreSpawn chunks).
                session.Protocol.Entity.SendLocalActorData((ulong)player.RuntimeId, player.Username);
                session.Protocol.Entity.SendDefaultAttributes(
                    (ulong)player.RuntimeId, player.Health, player.Hunger);
                // Abilities / adventure seed (§37) — after §34, before PreSpawn.
                session.Protocol.Entity.SendLocalAbilities(player.RuntimeId, (int)player.GameMode);
                session.Protocol.Entity.SendAdventureSettings();
                session.SetHandler(new PreSpawnSessionHandler());
                break;
        }

        return true;
    }
}
