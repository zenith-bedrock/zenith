using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>UpdateAdventureSettings (0xBC) — LAN defaults with UpdateAbilities (ADR §37).</summary>
sealed class UpdateAdventureSettingsPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.UPDATE_ADVENTURE_SETTINGS_PACKET;

    public bool NoPvM { get; set; }
    public bool NoMvP { get; set; }
    public bool ImmutableWorld { get; set; }
    public bool ShowNameTags { get; set; }
    public bool AutoJump { get; set; }

    public static UpdateAdventureSettingsPacket CreateLanDefaults() =>
        new()
        {
            NoPvM = false,
            NoMvP = false,
            ImmutableWorld = false,
            ShowNameTags = true,
            AutoJump = true
        };

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteBool(NoPvM);
        writer.WriteBool(NoMvP);
        writer.WriteBool(ImmutableWorld);
        writer.WriteBool(ShowNameTags);
        writer.WriteBool(AutoJump);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
