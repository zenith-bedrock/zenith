using Zenith.Binary;
using Zenith.Reference;

namespace Zenith.Packets;

/// <summary>
/// UnlockedRecipes (0xC7) — the recipe book contents: a little-endian u32 unlock type, then a varuint-counted list of
/// recipe id strings. BDS sends type <see cref="InitiallyUnlocked"/> once at join. Layout verified against the capture
/// (golden test).
/// </summary>
sealed class UnlockedRecipesPacket : DataPacket
{
    public const uint Empty = 0;
    public const uint InitiallyUnlocked = 1;
    public const uint NewlyUnlocked = 2;
    public const uint RemoveUnlocked = 3;
    public const uint RemoveAllUnlocked = 4;

    private static readonly Lazy<IReadOnlyList<string>> CapturedInitial = new(LoadCapturedInitial);

    public override int Id => (int)ProtocolInfo.UNLOCKED_RECIPES_PACKET;

    public uint UnlockType { get; set; } = InitiallyUnlocked;

    public IReadOnlyList<string> RecipeIds { get; set; } = [];

    /// <summary>The recipe ids stock BDS 1.26.63.1 unlocked for a new player, in its order (furnace recipes included).</summary>
    public static IReadOnlyList<string> InitialRecipeIdsOfBds => CapturedInitial.Value;

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUInt(UnlockType, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(RecipeIds.Count);
        foreach (var id in RecipeIds) writer.WriteVarString(id);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream)
    {
        UnlockType = stream.ReadUInt(BinaryStream.Endianess.Little);
        var ids = new List<string>();
        for (var n = stream.ReadUnsignedVarInt(); n > 0; n--) ids.Add(stream.ReadVarString());
        RecipeIds = ids;
    }

    private static IReadOnlyList<string> LoadCapturedInitial()
    {
        var entry = JoinStaticData.Named(JoinStaticNames.InitialRecipes).Single();
        var stream = new BinaryStream(entry.Body);
        try
        {
            var packet = new UnlockedRecipesPacket();
            packet.Decode(ref stream);
            if (packet.UnlockType != InitiallyUnlocked)
                throw new InvalidDataException($"The captured recipe book packet has unlock type {packet.UnlockType}, not initially-unlocked.");
            return packet.RecipeIds;
        }
        finally
        {
            stream.Dispose();
        }
    }
}
