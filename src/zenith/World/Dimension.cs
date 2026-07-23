namespace Zenith.World;

/// <summary>
/// One Bedrock dimension within a <see cref="World"/> (ADR §71 — Serenity/DF-shaped seam).
/// Holds terrain generator + wire id. Not a Serenity Entity/Feature container.
/// </summary>
sealed class Dimension
{
    /// <summary>Wire Overworld — equals <c>Packets.DimensionId.Overworld</c>.</summary>
    public const int OverworldWireId = 0;

    public string Identifier { get; }
    public int WireId { get; }
    public ITerrainProvider Terrain { get; }

    public Dimension(string identifier, int wireId, ITerrainProvider terrain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentNullException.ThrowIfNull(terrain);
        Identifier = identifier;
        WireId = wireId;
        Terrain = terrain;
    }

    public static Dimension CreateOverworld(ITerrainProvider terrain) =>
        new("overworld", OverworldWireId, terrain);
}
