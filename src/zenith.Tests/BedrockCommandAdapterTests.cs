using Zenith.Gameplay.Commands;
using Zenith.Server;
using Zenith.Server.Composition;
using Zenith.Player;
using Zenith.Packets;
using Zenith.Binary;
using Zenith.Session;
using Xunit;

namespace Zenith.Tests;

public sealed class BedrockCommandAdapterTests
{
    [Fact]
    public void Metadata_emits_valid_enum_symbols_and_bedrock_permission_tokens_to_eof()
    {
        var catalog = new CommandCatalog();
        catalog.Register(new CommandDefinition("admin", "op", "test.admin",
            new CommandOverload(new EnumCommandArgument("choice", new Dictionary<string, object> { ["one"] = 1 }))));
        catalog.Register(new CommandDefinition("public", "any", "test.any", new CommandOverload()));
        var packet = BedrockCommandAdapter.CreateMetadata(catalog, _ => true);
        Assert.Equal("any", packet.Commands[0].Permission);
        Assert.Equal(0x00300000u, packet.Commands[0].Overloads[0].Parameters[0].Type);
        var r = new BinaryStream(packet.Encode().ToArray());
        Assert.Equal(0x4c, r.ReadUnsignedVarInt());
        Assert.Equal(1, r.ReadUnsignedVarInt()); Assert.Equal("one", r.ReadVarString());
        Assert.Equal(0, r.ReadUnsignedVarInt()); // chained subcommand values
        Assert.Equal(0, r.ReadUnsignedVarInt()); // suffixes
        Assert.Equal(1, r.ReadUnsignedVarInt()); Assert.Equal("choice", r.ReadVarString());
        Assert.Equal(1, r.ReadUnsignedVarInt()); Assert.Equal(0u, r.ReadUInt(BinaryStream.Endianess.Little));
        Assert.Equal(0, r.ReadUnsignedVarInt()); // chained subcommands
        Assert.Equal(2, r.ReadUnsignedVarInt());
        foreach (var (name, description, permission) in new[] { ("admin", "op", "any"), ("public", "any", "any") })
        {
            Assert.Equal(name, r.ReadVarString()); Assert.Equal(description, r.ReadVarString());
            Assert.Equal(0, r.ReadUShort(BinaryStream.Endianess.Little));
            Assert.Equal(permission, r.ReadVarString());
            Assert.Equal(uint.MaxValue, r.ReadUInt(BinaryStream.Endianess.Little));
            Assert.Equal(0, r.ReadUnsignedVarInt()); Assert.Equal(1, r.ReadUnsignedVarInt());
            Assert.False(r.ReadBool());
            Assert.Equal(name == "admin" ? 1 : 0, r.ReadUnsignedVarInt());
            if (name == "admin")
            {
                Assert.Equal("choice", r.ReadVarString());
                // Independent BDS vector: enum index 34 is 22 00 30 00; index zero shares both flags.
                Assert.Equal(Convert.FromHexString("00003000"), r.ReadSpan(4).ToArray());
                Assert.False(r.ReadBool()); Assert.Equal(0, r.ReadByte());
            }
        }
        Assert.Equal(0, r.ReadUnsignedVarInt()); Assert.Equal(0, r.ReadUnsignedVarInt());
        Assert.True(r.IsEndOfFile);
        r.Dispose();
    }

    [Fact]
    public void CreateMetadata_projects_aliases_enums_optionals_targets_and_overloads()
    {
        var metadata = BedrockCommandAdapter.CreateMetadata(new CommandRuntime(new PlayerManager(), PermissionsComposition.Create(new ServerConfig())).Catalog, _ => true);

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
        var metadata = BedrockCommandAdapter.CreateMetadata(new CommandRuntime(new PlayerManager(), PermissionsComposition.Create(new ServerConfig())).Catalog, _ => true);
        var bytes = metadata.Encode();
        var stream = new BinaryStream(bytes.ToArray());

        Assert.Equal((int)ProtocolInfo.AVAILABLE_COMMANDS_PACKET, stream.ReadUnsignedVarInt());
        stream.Dispose();
    }

    [Fact]
    public void Metadata_lists_only_the_commands_the_player_may_use()
    {
        var catalog = new CommandCatalog();
        catalog.Register(new CommandDefinition("admin", "op", "test.admin", new CommandOverload()));
        catalog.Register(new CommandDefinition("public", "any", "test.any", new CommandOverload()));

        var packet = BedrockCommandAdapter.CreateMetadata(catalog, node => node == "test.any");

        Assert.Equal(["public"], packet.Commands.Select(command => command.Name));
    }
}
