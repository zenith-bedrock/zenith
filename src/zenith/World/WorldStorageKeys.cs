using System.Buffers.Binary;
using System.Text;

namespace Zenith.World;

/// <summary>
/// Zenith world LevelDB key formats (ADR §62; overlay/chest layout superseded by ADR §114's
/// resident-chunk-state implementation — see below). Prefixes/layouts are product schema — not
/// Mojang BDS wire format, though the overlay/chest binary layout mirrors BDS's own chunk-key shape
/// (fixed-width <c>chunkX:i32LE + chunkZ:i32LE + tag</c> prefix) so a per-chunk range scan is a
/// simple <c>Seek(prefix)</c> + <c>StartsWith(prefix)</c> walk, the same pattern real BDS uses.
/// Reserved (never write from Zenith backends): <c>player_</c>, <c>player_server_</c>, <c>~local_player</c>.
/// </summary>
static class WorldStorageKeys
{
    public const string ColumnPrefix = "c:";
    public const string InventoryPrefix = "inv:";
    public const string ArmorPrefix = "ar:";
    public const string PlayerDataPrefix = "pd:";

    /// <summary>Single fixed key — world generator identity (Phase XXIV, see <see cref="WorldMetadata"/>).</summary>
    public const string WorldMetadataKey = "wm:";

    private const byte OverlayTag = (byte)'O';
    private const byte ChestTag = (byte)'C';

    /// <summary>Fixed 15-byte layout: chunkX:i32LE(4) + chunkZ:i32LE(4) + tag:u8(1) + localX:u8(1)
    /// + y:i32LE(4) + localZ:u8(1). The 8-byte chunk-coord prefix is what a per-chunk scan seeks on
    /// — fixed width, not numeric sort order, is what makes prefix-matching negative coordinates work
    /// (same reasoning real BDS's LevelDB key layout relies on).</summary>
    private const int EntryKeyLength = 15;

    public static byte[] Column(ChunkCoord coord) =>
        Encoding.UTF8.GetBytes($"{ColumnPrefix}{coord.X}:{coord.Z}");

    /// <summary>First 8 bytes of every overlay/chest key for chunk (cx, cz) — the seek prefix.</summary>
    public static byte[] ChunkPrefix(int chunkX, int chunkZ)
    {
        var key = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(key.AsSpan(0, 4), chunkX);
        BinaryPrimitives.WriteInt32LittleEndian(key.AsSpan(4, 4), chunkZ);
        return key;
    }

    public static byte[] Overlay(int x, int y, int z) => EncodeEntry(OverlayTag, x, y, z);

    public static byte[] Chest(int x, int y, int z) => EncodeEntry(ChestTag, x, y, z);

    private static byte[] EncodeEntry(byte tag, int x, int y, int z)
    {
        var chunkX = ChunkMath.BlockToChunk(x);
        var chunkZ = ChunkMath.BlockToChunk(z);
        var key = new byte[EntryKeyLength];
        BinaryPrimitives.WriteInt32LittleEndian(key.AsSpan(0, 4), chunkX);
        BinaryPrimitives.WriteInt32LittleEndian(key.AsSpan(4, 4), chunkZ);
        key[8] = tag;
        key[9] = (byte)(x & 15);
        BinaryPrimitives.WriteInt32LittleEndian(key.AsSpan(10, 4), y);
        key[14] = (byte)(z & 15);
        return key;
    }

    public static bool TryParseOverlay(ReadOnlySpan<byte> key, out int x, out int y, out int z) =>
        TryParseEntry(key, OverlayTag, out x, out y, out z);

    public static bool TryParseChest(ReadOnlySpan<byte> key, out int x, out int y, out int z) =>
        TryParseEntry(key, ChestTag, out x, out y, out z);

    private static bool TryParseEntry(ReadOnlySpan<byte> key, byte expectedTag, out int x, out int y, out int z)
    {
        x = y = z = 0;
        if (key.Length != EntryKeyLength || key[8] != expectedTag) return false;

        var chunkX = BinaryPrimitives.ReadInt32LittleEndian(key.Slice(0, 4));
        var chunkZ = BinaryPrimitives.ReadInt32LittleEndian(key.Slice(4, 4));
        x = (chunkX << ChunkMath.ChunkSizeShift) | key[9];
        y = BinaryPrimitives.ReadInt32LittleEndian(key.Slice(10, 4));
        z = (chunkZ << ChunkMath.ChunkSizeShift) | key[14];
        return true;
    }

    public static byte[] Inventory(Guid uuid) =>
        Encoding.UTF8.GetBytes($"{InventoryPrefix}{uuid.ToString("D").ToLowerInvariant()}");

    public static byte[] Armor(Guid uuid) =>
        Encoding.UTF8.GetBytes($"{ArmorPrefix}{uuid.ToString("D").ToLowerInvariant()}");

    public static byte[] PlayerData(Guid uuid) =>
        Encoding.UTF8.GetBytes($"{PlayerDataPrefix}{uuid.ToString("D").ToLowerInvariant()}");
}
