namespace Zenith.World;

/// <summary>
/// Runtime IDs de bloco (hashes de estado Bedrock), alinhados ao formato de network palette.
/// Valores verificados no ecossistema de referência (FNV1a-32 do NBT le).
/// </summary>
static class Blocks
{
    public const int Air = unchecked((int)0xDBF44120); // -604749536
    public const int Stone = unchecked((int)0x80310E21); // -2144268767
    public const int GrassBlock = unchecked((int)0xDE3128B4); // -567203660

    public const int FlatMinY = -64;
    public const int FlatStoneTopY = -62;
    public const int FlatGrassY = -61;
    public const int FlatSpawnY = -60;
}
