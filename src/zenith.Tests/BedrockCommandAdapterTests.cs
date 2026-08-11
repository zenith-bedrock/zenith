using Zenith.Gameplay.Commands;
using Zenith.Player;
using Zenith.Packets;
using Zenith.Raknet.Stream;
using Zenith.Session;
using Xunit;

namespace Zenith.Tests;

public sealed class BedrockCommandAdapterTests
{
    [Fact]
    public void CreateMetadata_projects_aliases_enums_optionals_targets_and_overloads()
    {
        var metadata = BedrockCommandAdapter.CreateMetadata(new CommandRuntime(new PlayerManager()).Catalog);

        var gamemode = Assert.Single(metadata.Commands, command => command.Name == "gamemode");
        Assert.NotEqual(uint.MaxValue, gamemode.AliasEnumIndex);
        Assert.Single(gamemode.Overloads);
        Assert.Equal(2, gamemode.Overloads[0].Parameters.Count);
        Assert.True(gamemode.Overloads[0].Parameters[1].Optional);
        Assert.NotEmpty(metadata.Enums);
        Assert.Contains("creative", metadata.EnumValues);
    }

    [Fact]
    public void AvailableCommands_encode_starts_with_packet_id()
    {
        var metadata = BedrockCommandAdapter.CreateMetadata(new CommandRuntime(new PlayerManager()).Catalog);
        var bytes = metadata.Encode();
        var stream = new BinaryStream(bytes.ToArray());

        Assert.Equal((int)ProtocolInfo.AVAILABLE_COMMANDS_PACKET, stream.ReadUnsignedVarInt());
        stream.Dispose();
    }
}
