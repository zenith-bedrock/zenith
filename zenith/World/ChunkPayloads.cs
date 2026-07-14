using Zenith.Raknet.Stream;

namespace Zenith.World;

/// <summary>Gera payload de coluna overworld vazia (biomes + border). Sem DataPacket.</summary>
static class ChunkPayloads
{
    private const int OverworldMinSubChunkIndex = -4;
    private const int OverworldMaxSubChunkIndex = 19;
    private const int OverworldSubChunkCount = OverworldMaxSubChunkIndex - OverworldMinSubChunkIndex + 1;
    private const int PlainsBiomeId = 1;

    public static byte[] BuildEmptyOverworld()
    {
        var writer = new BinaryStream();
        for (var i = 0; i < OverworldSubChunkCount; i++)
        {
            writer.WriteByte(1);
            writer.WriteVarInt(PlainsBiomeId);
        }

        writer.WriteByte(0);
        return writer.GetBufferDisposing().ToArray();
    }
}
