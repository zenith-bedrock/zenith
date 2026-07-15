using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>UpdateAbilities (0xBB) — local ability seed / refresh (ADR §37).</summary>
sealed class UpdateAbilitiesPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.UPDATE_ABILITIES_PACKET;

    public long UniqueId { get; set; }
    public uint Values { get; set; }

    public static UpdateAbilitiesPacket Create(long uniqueId, int wireGameMode, bool flying = true) =>
        new()
        {
            UniqueId = uniqueId,
            Values = AbilityData.ValuesForGameMode(wireGameMode, flying)
        };

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        AbilityData.Write(ref writer, UniqueId, Values);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
