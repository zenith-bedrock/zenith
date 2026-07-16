using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

public class CommandPacketTests
{
    [Fact]
    public void CommandRequest_encode_decode_roundtrip()
    {
        var origin = new CommandOriginData
        {
            CommandType = "Player",
            CommandUuid = Guid.NewGuid().ToString(),
            RequestId = Guid.NewGuid().ToString(),
            PlayerActorUniqueId = 42
        };

        var packet = new CommandRequestPacket
        {
            Command = "help",
            OriginData = origin,
            IsInternal = false,
            Version = "1.26.33"
        };

        var bytes = packet.Encode().ToArray();

        var stream = new BinaryStream(bytes);
        var decoded = DataPacket.From<CommandRequestPacket>(ref stream);

        Assert.Equal(packet.Command, decoded.Command);
        Assert.Equal(packet.OriginData.CommandType, decoded.OriginData.CommandType);
        Assert.Equal(packet.OriginData.CommandUuid, decoded.OriginData.CommandUuid);
        Assert.Equal(packet.OriginData.RequestId, decoded.OriginData.RequestId);
        Assert.Equal(packet.OriginData.PlayerActorUniqueId, decoded.OriginData.PlayerActorUniqueId);
        Assert.Equal(packet.IsInternal, decoded.IsInternal);
        Assert.Equal(packet.Version, decoded.Version);
    }

    [Fact]
    public void CommandOutput_encode_decode_roundtrip_all()
    {
        var origin = new CommandOriginData
        {
            CommandType = "Player",
            CommandUuid = Guid.NewGuid().ToString(),
            RequestId = Guid.NewGuid().ToString(),
            PlayerActorUniqueId = 0
        };

        var packet = new CommandOutputPacket
        {
            OriginData = origin,
            OutputType = CommandOutputType.TYPE_ALL,
            SuccessCount = 1,
            Messages =
            [
                new CommandOutputMessage
                {
                    IsInternal = false,
                    MessageId = "commands.generic.success",
                    Parameters = []
                }
            ],
            CommandData = "help"
        };

        var bytes = packet.Encode().ToArray();

        var stream = new BinaryStream(bytes);
        var decoded = DataPacket.From<CommandOutputPacket>(ref stream);

        Assert.Equal(packet.OriginData.CommandType, decoded.OriginData.CommandType);
        Assert.Equal(packet.OriginData.CommandUuid, decoded.OriginData.CommandUuid);
        Assert.Equal(packet.OriginData.RequestId, decoded.OriginData.RequestId);
        Assert.Equal(packet.OutputType, decoded.OutputType);
        Assert.Equal(packet.SuccessCount, decoded.SuccessCount);
        Assert.Single(decoded.Messages);
        Assert.Equal(packet.Messages[0].IsInternal, decoded.Messages[0].IsInternal);
        Assert.Equal(packet.Messages[0].MessageId, decoded.Messages[0].MessageId);
        Assert.Empty(decoded.Messages[0].Parameters);
        Assert.Empty(decoded.CommandData); // TYPE_ALL → CommandData not written → empty on decode
    }

    [Fact]
    public void CommandOutput_encode_decode_roundtrip_data_set()
    {
        var origin = new CommandOriginData
        {
            CommandType = "Player",
            CommandUuid = Guid.NewGuid().ToString(),
            RequestId = Guid.NewGuid().ToString(),
            PlayerActorUniqueId = 0
        };

        var packet = new CommandOutputPacket
        {
            OriginData = origin,
            OutputType = CommandOutputType.TYPE_DATA_SET,
            SuccessCount = 0,
            Messages = [],
            CommandData = "{\"key\":\"value\"}"
        };

        var bytes = packet.Encode().ToArray();

        var stream = new BinaryStream(bytes);
        var decoded = DataPacket.From<CommandOutputPacket>(ref stream);

        Assert.Equal(CommandOutputType.TYPE_DATA_SET, decoded.OutputType);
        Assert.Equal(packet.CommandData, decoded.CommandData);
    }

    [Fact]
    public void AvailableCommands_encode_decode_roundtrip()
    {
        var packet = new AvaliableCommandsPacket
        {
            EnumValues = new CommandEnumValues { EnumValues = ["help", "about"] },
            ChainedSubCommandValues = new ChainedSubCommandValues { ChainedValues = [] },
            PostFixes = new CommandPostFixes { PostFixes = [] },
            EnumData = new CommandEnumData
            {
                EnumDataValues =
                [
                    new CommandEnumValue { Name = "CommandNames", Values = [0u, 1u] }
                ]
            },
            SubCommandData = [],
            CommandData =
            [
                new Commands
                {
                    Name = "help",
                    Description = "Shows available commands",
                    Flags = 0,
                    PermissionLevel = "Any",
                    Alias = -1,
                    SubCommands = [],
                    Overloads = []
                },
                new Commands
                {
                    Name = "about",
                    Description = "About the software",
                    Flags = 0,
                    PermissionLevel = "Admin",
                    Alias = -1,
                    SubCommands = [],
                    Overloads = []
                }
            ],
            SoftEnums =
            [
                new SoftEnumData { Name = "PlayerNames", Options = ["Steve"] }
            ],
            Constraints = []
        };

        var bytes = packet.Encode().ToArray();

        var stream = new BinaryStream(bytes);
        var decoded = DataPacket.From<AvaliableCommandsPacket>(ref stream);

        Assert.Equal(2, decoded.EnumValues.EnumValues.Length);
        Assert.Equal("help", decoded.EnumValues.EnumValues[0]);
        Assert.Equal("about", decoded.EnumValues.EnumValues[1]);

        Assert.Single(decoded.EnumData.EnumDataValues);
        Assert.Equal("CommandNames", decoded.EnumData.EnumDataValues[0].Name);
        Assert.Equal(2, decoded.EnumData.EnumDataValues[0].Values.Length);

        Assert.Equal(2, decoded.CommandData.Length);
        Assert.Equal("help", decoded.CommandData[0].Name);
        Assert.Equal("Shows available commands", decoded.CommandData[0].Description);
        Assert.Equal("Any", decoded.CommandData[0].PermissionLevel);
        Assert.Equal(-1, decoded.CommandData[0].Alias);

        Assert.Equal("about", decoded.CommandData[1].Name);
        Assert.Equal("About the software", decoded.CommandData[1].Description);
        Assert.Equal("Admin", decoded.CommandData[1].PermissionLevel);
        Assert.Equal(-1, decoded.CommandData[1].Alias);

        Assert.Single(decoded.SoftEnums);
        Assert.Equal("PlayerNames", decoded.SoftEnums[0].Name);
        Assert.Equal("Steve", decoded.SoftEnums[0].Options[0]);
    }

    [Fact]
    public void CommandOutput_wire_outputType_is_byte()
    {
        var origin = new CommandOriginData
        {
            CommandType = "Player",
            CommandUuid = Guid.NewGuid().ToString(),
            RequestId = Guid.NewGuid().ToString(),
            PlayerActorUniqueId = 0
        };

        var packet = new CommandOutputPacket
        {
            OriginData = origin,
            OutputType = CommandOutputType.TYPE_ALL,
            SuccessCount = 1,
            Messages =
            [
                new CommandOutputMessage
                {
                    IsInternal = false,
                    MessageId = "test",
                    Parameters = []
                }
            ],
            CommandData = ""
        };

        var bytes = packet.Encode().ToArray();
        var stream = new BinaryStream(bytes);

        // Skip origin data (VarString + UUID + VarString [+ VarLong])
        stream.ReadVarString();
        stream.ReadUuid();
        stream.ReadVarString();

        Assert.Equal((byte)CommandOutputType.TYPE_ALL, stream.ReadByte());
    }

    [Fact]
    public void AvailableCommands_permissionLevel_is_string()
    {
        var packet = new AvaliableCommandsPacket
        {
            EnumValues = new CommandEnumValues { EnumValues = ["test"] },
            ChainedSubCommandValues = new ChainedSubCommandValues { ChainedValues = [] },
            PostFixes = new CommandPostFixes { PostFixes = [] },
            EnumData = new CommandEnumData { EnumDataValues = [] },
            SubCommandData = [],
            CommandData =
            [
                new Commands
                {
                    Name = "test",
                    Description = "Test command",
                    Flags = 0,
                    PermissionLevel = "Host",
                    Alias = -1,
                    SubCommands = [],
                    Overloads = []
                }
            ],
            SoftEnums = [],
            Constraints = []
        };

        var bytes = packet.Encode().ToArray();
        var stream = new BinaryStream(bytes);

        // Skip fields before CommandData (counts are unsigned VarInt)
        stream.ReadUnsignedVarInt(); // enumValues count
        stream.ReadUnsignedVarInt(); // chainedSubCommandValues count
        stream.ReadUnsignedVarInt(); // postFixes count
        stream.ReadUnsignedVarInt(); // enumData count

        var cmdCount = stream.ReadUnsignedVarInt();
        Assert.Equal(1u, cmdCount);

        stream.ReadVarString(); // name
        stream.ReadVarString(); // description
        stream.ReadUShort(BinaryStream.Endianess.Little); // flags
        var perm = stream.ReadVarString();

        Assert.Equal("Host", perm);
    }

    [Fact]
    public void CommandOutput_count_uses_unsigned_varint()
    {
        var origin = new CommandOriginData
        {
            CommandType = "Player",
            CommandUuid = Guid.NewGuid().ToString(),
            RequestId = Guid.NewGuid().ToString(),
            PlayerActorUniqueId = 0
        };

        var packet = new CommandOutputPacket
        {
            OriginData = origin,
            OutputType = CommandOutputType.TYPE_ALL,
            SuccessCount = 3,
            Messages =
            [
                new CommandOutputMessage { IsInternal = false, MessageId = "a", Parameters = ["x", "y"] },
                new CommandOutputMessage { IsInternal = false, MessageId = "b", Parameters = [] }
            ],
            CommandData = ""
        };

        var bytes = packet.Encode().ToArray();
        var stream = new BinaryStream(bytes);

        // Skip origin
        stream.ReadVarString();
        stream.ReadUuid();
        stream.ReadVarString();

        // outputType (byte)
        Assert.Equal((byte)CommandOutputType.TYPE_ALL, stream.ReadByte());

        // SuccessCount = 3 → unsigned VarInt → raw byte 0x03 (NOT ZigZag(3)=6 → 0x06)
        Assert.Equal(3, stream.ReadUnsignedVarInt());

        // Messages.Length = 2 → unsigned VarInt → raw byte 0x02 (NOT ZigZag(2)=4 → 0x04)
        Assert.Equal(2, stream.ReadUnsignedVarInt());
    }

    [Fact]
    public void AvailableCommands_counts_use_unsigned_varint()
    {
        var packet = new AvaliableCommandsPacket
        {
            EnumValues = new CommandEnumValues { EnumValues = ["a", "b", "c"] },
            ChainedSubCommandValues = new ChainedSubCommandValues { ChainedValues = [] },
            PostFixes = new CommandPostFixes { PostFixes = [] },
            EnumData = new CommandEnumData
            {
                EnumDataValues =
                [
                    new CommandEnumValue { Name = "E1", Values = [0u, 1u] }
                ]
            },
            SubCommandData = [],
            CommandData =
            [
                new Commands
                {
                    Name = "c1", Description = "", Flags = 0, PermissionLevel = "Any",
                    Alias = -1, SubCommands = [], Overloads = []
                }
            ],
            SoftEnums = [],
            Constraints = []
        };

        var bytes = packet.Encode().ToArray();
        var stream = new BinaryStream(bytes);

        // enumValues count = 3 → unsigned VarInt → raw 0x03
        Assert.Equal(3, stream.ReadUnsignedVarInt());

        // chainedSubCommandValues count = 0 → unsigned VarInt → raw 0x00
        Assert.Equal(0, stream.ReadUnsignedVarInt());

        // postFixes count = 0 → unsigned VarInt → raw 0x00
        Assert.Equal(0, stream.ReadUnsignedVarInt());

        // enumData count = 1 → unsigned VarInt → raw 0x01
        Assert.Equal(1, stream.ReadUnsignedVarInt());

        // SubCommandData count = 0 (outer, uses UnsignedVarInt in packet class)
        Assert.Equal(0, stream.ReadUnsignedVarInt());

        // CommandData count = 1 (outer, uses UnsignedVarInt in packet class)
        Assert.Equal(1, stream.ReadUnsignedVarInt());

        // Within command: SubCommands.Length = 0 (unsigned)
        // Skip name, desc, flags, permLevel, alias → then read subCount
        stream.ReadVarString(); // name
        stream.ReadVarString(); // desc
        stream.ReadUShort(BinaryStream.Endianess.Little); // flags
        stream.ReadVarString(); // permLevel
        stream.ReadInt(BinaryStream.Endianess.Little); // alias
        Assert.Equal(0, stream.ReadUnsignedVarInt()); // subCommands count = 0

        // Overloads count = 0 (unsigned)
        Assert.Equal(0, stream.ReadUnsignedVarInt());
    }
}
