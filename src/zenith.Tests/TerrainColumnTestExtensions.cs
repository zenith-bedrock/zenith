using Zenith.World;

namespace Zenith.Tests;

/// <summary>Test view of a freshly generated <see cref="TerrainColumn"/> as the LevelChunk body a client would get.</summary>
static class TerrainColumnTestExtensions
{
    public static WirePayload Wire(this TerrainColumn terrain)
    {
        var column = new WorldColumn(new ChunkCoord(0, 0), Dimension.OverworldWireId, terrain.BiomeNetworkId);
        for (var i = 0; i < terrain.Sections.Length; i++)
            if (terrain.Sections[i] is { } section) column.SetSection(i, section);
        return column.GetWire();
    }
}
