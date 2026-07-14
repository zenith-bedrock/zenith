namespace Zenith.World;

/// <summary>
/// Mapa mínimo block runtime → network item id (item_palette.json).
/// Sem registry comportamental (Food/Sword); só o necessário ao wire.
/// </summary>
static class Items
{
    // From data/item_palette.json
    public const short AirNetworkId = 0;
    public const short StoneNetworkId = 1;
    public const short GrassBlockNetworkId = 2;

    public static short NetworkIdForBlock(int blockRuntimeId)
    {
        if (blockRuntimeId == Blocks.Air) return AirNetworkId;
        if (blockRuntimeId == Blocks.Stone) return StoneNetworkId;
        if (blockRuntimeId == Blocks.GrassBlock) return GrassBlockNetworkId;
        return StoneNetworkId; // unknown placeable → treat as stone-like slot item
    }
}
