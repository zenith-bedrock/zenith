using System.Text;
using Zenith.Network.Packets;
using Xunit;

namespace Zenith.Tests;

public class HudSpawnPacketTests
{
    [Fact]
    public void SetActorData_encode_includes_breathing_metadata()
    {
        var bytes = new SetActorDataPacket
        {
            ActorRuntimeId = 2,
            Name = "tester",
            Tick = 0
        }.Encode().ToArray();

        Assert.True(bytes.Length > 16);
        // Username string appears in metadata payload
        Assert.Contains("tester", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void UpdateAttributes_frozen_defaults_encode_health_and_hunger()
    {
        var packet = UpdateAttributesPacket.CreateFrozenDefaults(2);
        Assert.True(packet.Attributes.Length >= 2);
        Assert.Contains(packet.Attributes, a => a.Name == "minecraft:health");
        Assert.Contains(packet.Attributes, a => a.Name == "minecraft:player.hunger");

        var bytes = packet.Encode().ToArray();
        Assert.True(bytes.Length > 32);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("minecraft:health", text);
        Assert.Contains("minecraft:player.hunger", text);
    }

    [Fact]
    public void EntityMetadataWriter_visible_name_is_non_empty()
    {
        var writer = new Zenith.Raknet.Stream.BinaryStream();
        EntityMetadataWriter.WriteVisibleNameMetadata(ref writer, "a");
        Assert.True(writer.GetBufferDisposing().Length > 8);
    }
}
