using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

public class ResourcePacksInfoPacketTests
{
    [Fact]
    public void Encode_writes_varint_texture_pack_count_not_fixed_short()
    {
        var bytes = new ResourcePacksInfoPacket
        {
            MustAccept = false,
            HasAddons = false,
            HasScripts = false,
            ForceDisableVibrantVisuals = false,
            WorldTemplateVersion = ""
        }.Encode().ToArray();

        var stream = new BinaryStream(bytes);
        Assert.Equal((int)ProtocolInfo.RESOURCE_PACKS_INFO_PACKET, stream.ReadUnsignedVarInt());
        Assert.False(stream.ReadBool()); // must_accept
        Assert.False(stream.ReadBool()); // has_addons
        Assert.False(stream.ReadBool()); // has_scripts
        Assert.False(stream.ReadBool()); // disable_vibrant_visuals
        Assert.Equal(Guid.Empty, stream.ReadUuid()); // world_template.uuid
        Assert.Equal("", stream.ReadVarString()); // world_template.version
        Assert.Equal(0, (int)stream.ReadUnsignedVarInt()); // texture_packs count (varint, ADR §90)
        Assert.True(stream.IsEndOfFile);
    }
}
