using System.Text;

namespace Zenith.World;

/// <summary>
/// Zenith world LevelDB key formats (ADR §62, §167). Prefixes/layouts are product schema — not Mojang BDS format.
/// Reserved (never write from Zenith backends): <c>player_</c>, <c>player_server_</c>, <c>~local_player</c>.
/// </summary>
static class WorldStorageKeys
{
    public const string ColumnPrefix = "c:";
    public const string InventoryPrefix = "inv:";
    public const string ArmorPrefix = "ar:";
    public const string OffhandPrefix = "oh:";
    public const string PlayerDataPrefix = "pd:";

    /// <summary>Single fixed key — world generator identity (Phase XXIV, see <see cref="WorldMetadata"/>).</summary>
    public const string WorldMetadataKey = "wm:";

    /// <summary>Single fixed key — the world's game rules, in the wire encoding (<see cref="GameRuleCodec"/>).</summary>
    public const string GameRulesKey = "gr:";

    public static byte[] Column(ChunkCoord coord) =>
        Encoding.UTF8.GetBytes($"{ColumnPrefix}{coord.X}:{coord.Z}");

    public static byte[] Inventory(Guid uuid) =>
        Encoding.UTF8.GetBytes($"{InventoryPrefix}{uuid.ToString("D").ToLowerInvariant()}");

    public static byte[] Armor(Guid uuid) =>
        Encoding.UTF8.GetBytes($"{ArmorPrefix}{uuid.ToString("D").ToLowerInvariant()}");

    public static byte[] Offhand(Guid uuid) =>
        Encoding.UTF8.GetBytes($"{OffhandPrefix}{uuid.ToString("D").ToLowerInvariant()}");

    public static byte[] PlayerData(Guid uuid) =>
        Encoding.UTF8.GetBytes($"{PlayerDataPrefix}{uuid.ToString("D").ToLowerInvariant()}");
}
