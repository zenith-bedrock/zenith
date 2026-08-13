using System.Text;

namespace Zenith.World;

/// <summary>
/// Zenith world LevelDB key formats (ADR §62). Prefixes are product schema — not Mojang BDS.
/// Reserved (never write from Zenith backends): <c>player_</c>, <c>player_server_</c>, <c>~local_player</c>.
/// </summary>
static class WorldStorageKeys
{
    public const string ColumnPrefix = "c:";
    public const string OverlayPrefix = "ov:";
    public const string ChestPrefix = "ct:";
    public const string InventoryPrefix = "inv:";
    public const string ArmorPrefix = "ar:";
    public const string PlayerDataPrefix = "pd:";

    public static readonly byte[] OverlayPrefixBytes = Encoding.UTF8.GetBytes(OverlayPrefix);
    public static readonly byte[] ChestPrefixBytes = Encoding.UTF8.GetBytes(ChestPrefix);

    public static byte[] Column(ChunkCoord coord) =>
        Encoding.UTF8.GetBytes($"{ColumnPrefix}{coord.X}:{coord.Z}");

    public static byte[] Overlay(int x, int y, int z) =>
        Encoding.UTF8.GetBytes($"{OverlayPrefix}{x}:{y}:{z}");

    public static byte[] Chest(int x, int y, int z) =>
        Encoding.UTF8.GetBytes($"{ChestPrefix}{x}:{y}:{z}");

    public static byte[] Inventory(Guid uuid) =>
        Encoding.UTF8.GetBytes($"{InventoryPrefix}{uuid.ToString("D").ToLowerInvariant()}");

    public static byte[] Armor(Guid uuid) =>
        Encoding.UTF8.GetBytes($"{ArmorPrefix}{uuid.ToString("D").ToLowerInvariant()}");

    public static byte[] PlayerData(Guid uuid) =>
        Encoding.UTF8.GetBytes($"{PlayerDataPrefix}{uuid.ToString("D").ToLowerInvariant()}");

    public static bool TryParseOverlay(string key, out int x, out int y, out int z)
    {
        x = y = z = 0;
        if (!key.StartsWith(OverlayPrefix, StringComparison.Ordinal)) return false;
        return TryParseXyz(key.AsSpan(OverlayPrefix.Length), out x, out y, out z);
    }

    public static bool TryParseChest(string key, out int x, out int y, out int z)
    {
        x = y = z = 0;
        if (!key.StartsWith(ChestPrefix, StringComparison.Ordinal)) return false;
        return TryParseXyz(key.AsSpan(ChestPrefix.Length), out x, out y, out z);
    }

    private static bool TryParseXyz(ReadOnlySpan<char> s, out int x, out int y, out int z)
    {
        x = y = z = 0;
        var sep1 = s.IndexOf(':');
        if (sep1 < 0 || !int.TryParse(s[..sep1], out x)) return false;
        s = s[(sep1 + 1)..];
        var sep2 = s.IndexOf(':');
        if (sep2 < 0 || !int.TryParse(s[..sep2], out y)) return false;
        return int.TryParse(s[(sep2 + 1)..], out z);
    }
}
