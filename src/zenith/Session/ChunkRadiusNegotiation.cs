using Zenith.Binary;
using Zenith.Packets;

namespace Zenith.Session;

/// <summary>Radius negotiation is valid throughout terrain loading and play; only the tick changes streaming state.</summary>
static class ChunkRadiusNegotiation
{
    public static void HandleRequest(NetworkSession session, ref BinaryStream stream, bool startPreSpawn = false)
    {
        var request = DataPacket.From<RequestChunkRadiusPacket>(ref stream);
        var player = session.Player;
        if (player is null) return;
        var radius = Math.Clamp(request.Radius, 0, session.Context.Config.World.SpawnChunkRadius);
        session.Protocol.World.SendChunkRadiusUpdated(radius);

        if (startPreSpawn && Interlocked.CompareExchange(ref session.PreSpawnLoadStarted, 1, 0) == 0)
        {
            _ = player.Chunks.TrySubmitPreSpawn(radius);
        }
        else
        {
            player.Chunks.SubmitRadius(radius);
        }
        session.Context.Logger.Debug($"Chunk radius negotiated for {player.Username}: requested={request.Radius}, accepted={radius}");
    }
}
