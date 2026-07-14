namespace Zenith.Network.Packets;

/// <summary>
/// Facade legado; payload empty vive em <c>Zenith.World.ChunkPayloads</c>.
/// </summary>
static class ChunkUtils
{
    public static byte[] BuildEmptyOverworldPayload() => World.ChunkPayloads.BuildEmptyOverworld();
}
