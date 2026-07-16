using Zenith.Raknet.Stream;

namespace Zenith.Packets;

readonly struct CommandEnumValues
{
    public string[] EnumValues { get; init; }

    public static CommandEnumValues Read(ref BinaryStream stream)
    {
        var count = stream.ReadUnsignedVarInt();
        var values = new string[count];
        for (var i = 0; i < count; i++)
            values[i] = stream.ReadVarString();
        return new CommandEnumValues { EnumValues = values };
    }

    public void Write(BinaryStream stream)
    {
        stream.WriteUnsignedVarInt(EnumValues.Length);
        foreach (var enumValue in EnumValues)
            stream.WriteVarString(enumValue);
    }
}

readonly struct ChainedSubCommandValues
{
    public string[] ChainedValues { get; init; }

    public static ChainedSubCommandValues Read(ref BinaryStream stream)
    {
        var count = stream.ReadUnsignedVarInt();
        var values = new string[count];
        for (var i = 0; i < count; i++)
            values[i] = stream.ReadVarString();
        return new ChainedSubCommandValues { ChainedValues = values };
    }

    public void Write(BinaryStream stream)
    {
        stream.WriteUnsignedVarInt(ChainedValues.Length);
        foreach (var chainedValue in ChainedValues)
            stream.WriteVarString(chainedValue);
    }
}

readonly struct CommandPostFixes
{
    public string[] PostFixes { get; init; }

    public static CommandPostFixes Read(ref BinaryStream stream)
    {
        var count = stream.ReadUnsignedVarInt();

        var values = new string[count];
        for (var i = 0; i < count; i++)
            values[i] = stream.ReadVarString();

        return new CommandPostFixes { PostFixes = values };
    }

    public void Write(BinaryStream writer)
    {
        writer.WriteUnsignedVarInt(PostFixes.Length);
        foreach (var postFix in PostFixes)
            writer.WriteVarString(postFix);
    }
}

internal struct CommandEnumValue
{
    public string Name { get; set; }
    public uint[] Values { get; set; }
}

readonly struct CommandEnumData
{
    public CommandEnumValue[] EnumDataValues { get; init; }

    public static CommandEnumData Read(ref BinaryStream stream)
    {
        var count = stream.ReadUnsignedVarInt();
        var values = new CommandEnumValue[count];
        for (var i = 0; i < count; i++)
        {
            var name = stream.ReadVarString();
            var valCount = stream.ReadUnsignedVarInt();

            var vals = new uint[valCount];
            for (var j = 0; j < valCount; j++)
                vals[j] = stream.ReadUInt(BinaryStream.Endianess.Little);

            values[i] = new CommandEnumValue { Name = name, Values = vals };
        }
        return new CommandEnumData { EnumDataValues = values };
    }

    public void Write(BinaryStream writer)
    {
        writer.WriteUnsignedVarInt(EnumDataValues.Length);
        foreach (var ev in EnumDataValues)
        {
            writer.WriteVarString(ev.Name);
            writer.WriteUnsignedVarInt(ev.Values.Length);
            foreach (var v in ev.Values)
                writer.WriteUInt(v, BinaryStream.Endianess.Little);
        }
    }
}

internal struct CommandSubCommandValue
{
    public ushort Index { get; init; }
    public ushort Value { get; init; }
}

readonly struct CommandSubCommandData
{
    public string Name { get; init; }
    public CommandSubCommandValue[] Values { get; init; }

    public static CommandSubCommandData Read(ref BinaryStream stream)
    {
        var name = stream.ReadVarString();
        var count = stream.ReadUnsignedVarInt();
        var values = new CommandSubCommandValue[count];
        for (var i = 0; i < count; i++)
            values[i] = new CommandSubCommandValue
            {
                Index = stream.ReadUShort(BinaryStream.Endianess.Little),
                Value = stream.ReadUShort(BinaryStream.Endianess.Little)
            };
        return new CommandSubCommandData { Name = name, Values = values };
    }
}

readonly struct Commands
{
    internal struct CommandOverloadParameter
    {
        public uint Symbol { get; init; }
        public string Name { get; init; }
        public bool Optional { get; init; }
        public byte Options { get; init; }
    }

    internal struct CommandOverload
    {
        public bool Chaining { get; init; }
        public CommandOverloadParameter[] Parameters { get; init; }
    }

    public string Name { get; init; }
    public string Description { get; init; }
    public ushort Flags { get; init; }
    public string PermissionLevel { get; init; }
    public int Alias { get; init; }
    public ushort[] SubCommands { get; init; }
    public CommandOverload[] Overloads { get; init; }

    public static Commands Read(ref BinaryStream stream)
    {
        var name = stream.ReadVarString();
        var description = stream.ReadVarString();
        var flags = stream.ReadUShort(BinaryStream.Endianess.Little);
        var permissionLevel = stream.ReadVarString();
        var alias = stream.ReadInt(BinaryStream.Endianess.Little);

        var subCount = stream.ReadUnsignedVarInt();
        var subcommands = new ushort[subCount];
        for (var i = 0; i < subCount; i++)
            subcommands[i] = stream.ReadUShort(BinaryStream.Endianess.Little);

        var overloadCount = stream.ReadUnsignedVarInt();
        var overloads = new CommandOverload[overloadCount];
        for (var i = 0; i < overloadCount; i++)
        {
            var chaining = stream.ReadBool();
            var paramCount = stream.ReadUnsignedVarInt();
            var parameters = new CommandOverloadParameter[paramCount];
            for (var j = 0; j < paramCount; j++)
            {
                parameters[j] = new CommandOverloadParameter
                {
                    Name = stream.ReadVarString(),
                    Symbol = stream.ReadUInt(BinaryStream.Endianess.Little),
                    Optional = stream.ReadBool(),
                    Options = stream.ReadByte()
                };
            }
            overloads[i] = new CommandOverload { Chaining = chaining, Parameters = parameters };
        }

        return new Commands
        {
            Name = name,
            Description = description,
            Flags = flags,
            PermissionLevel = permissionLevel,
            Alias = alias,
            SubCommands = subcommands,
            Overloads = overloads
        };
    }

    public void Write(BinaryStream writer)
    {
        writer.WriteVarString(Name);
        writer.WriteVarString(Description);
        writer.WriteUShort(Flags, BinaryStream.Endianess.Little);
        writer.WriteVarString(PermissionLevel);
        writer.WriteInt(Alias, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(SubCommands.Length);
        foreach (var s in SubCommands)
            writer.WriteUShort(s, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(Overloads.Length);
        foreach (var o in Overloads)
        {
            writer.WriteBool(o.Chaining);
            writer.WriteUnsignedVarInt(o.Parameters.Length);
            foreach (var p in o.Parameters)
            {
                writer.WriteVarString(p.Name);
                writer.WriteUInt(p.Symbol, BinaryStream.Endianess.Little);
                writer.WriteBool(p.Optional);
                writer.WriteByte(p.Options);
            }
        }
    }
}

readonly struct SoftEnumData
{
    public string Name { get; init; }
    public string[] Options { get; init; }

    public static SoftEnumData Read(ref BinaryStream stream)
    {
        var name = stream.ReadVarString();
        var count = stream.ReadUnsignedVarInt();
        var options = new string[count];
        for (var i = 0; i < count; i++)
            options[i] = stream.ReadVarString();
        return new SoftEnumData { Name = name, Options = options };
    }

    public void Write(BinaryStream writer)
    {
        writer.WriteVarString(Name);
        writer.WriteUnsignedVarInt(Options.Length);
        foreach (var o in Options)
            writer.WriteVarString(o);
    }
}

readonly struct ConstrainedValueData
{
    public uint ValueSymbol { get; init; }
    public uint EnumSymbol { get; init; }
    public byte[] Constraints { get; init; }

    public static ConstrainedValueData Read(ref BinaryStream stream)
    {
        var valueSymbol = stream.ReadUInt(BinaryStream.Endianess.Little);
        var enumSymbol = stream.ReadUInt(BinaryStream.Endianess.Little);
        var count = stream.ReadUnsignedVarInt();
        var constraints = new byte[count];
        for (var i = 0; i < count; i++)
            constraints[i] = stream.ReadByte();
        return new ConstrainedValueData
        {
            ValueSymbol = valueSymbol,
            EnumSymbol = enumSymbol,
            Constraints = constraints
        };
    }

    public void Write(BinaryStream writer)
    {
        writer.WriteUInt(ValueSymbol, BinaryStream.Endianess.Little);
        writer.WriteUInt(EnumSymbol, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(Constraints.Length);
        foreach (var c in Constraints)
            writer.WriteByte(c);
    }
}

sealed class AvaliableCommandsPacket : DataPacket
{
    public const string PERMISSION_LEVEL_ANY = "Any";
    public const string PERMISSION_LEVEL_GAMEDIRECTORS = "GameDirectors";
    public const string PERMISSION_LEVEL_ADMIN = "Admin";
    public const string PERMISSION_LEVEL_HOST = "Host";
    public const string PERMISSION_LEVEL_OWNER = "Owner";
    public const string PERMISSION_LEVEL_INTERNAL = "Internal";

    public override int Id => (int)ProtocolInfo.AVALIABLE_COMMANDS_PACKET;

    public CommandEnumValues EnumValues { get; set; }
    public ChainedSubCommandValues ChainedSubCommandValues { get; set; }
    public CommandPostFixes PostFixes { get; set; }
    public CommandEnumData EnumData { get; set; }
    public CommandSubCommandData[] SubCommandData { get; set; }
    public Commands[] CommandData { get; set; }
    public SoftEnumData[] SoftEnums { get; set; }
    public ConstrainedValueData[] Constraints { get; set; }

    public override void Decode(ref BinaryStream stream)
    {
        EnumValues = CommandEnumValues.Read(ref stream);
        ChainedSubCommandValues = ChainedSubCommandValues.Read(ref stream);
        PostFixes = CommandPostFixes.Read(ref stream);
        EnumData = CommandEnumData.Read(ref stream);

        var chainedCount = stream.ReadUnsignedVarInt();
        var chained = new CommandSubCommandData[chainedCount];
        for (var i = 0; i < chainedCount; i++)
            chained[i] = CommandSubCommandData.Read(ref stream);
        SubCommandData = chained;

        var cmdCount = stream.ReadUnsignedVarInt();
        var commands = new Commands[cmdCount];
        for (var i = 0; i < cmdCount; i++)
            commands[i] = Commands.Read(ref stream);
        CommandData = commands;

        var softCount = stream.ReadUnsignedVarInt();
        var softs = new SoftEnumData[softCount];
        for (var i = 0; i < softCount; i++)
            softs[i] = SoftEnumData.Read(ref stream);
        SoftEnums = softs;

        var constCount = stream.ReadUnsignedVarInt();
        var constraints = new ConstrainedValueData[constCount];
        for (var i = 0; i < constCount; i++)
            constraints[i] = ConstrainedValueData.Read(ref stream);
        Constraints = constraints;
    }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        EnumValues.Write(writer);
        ChainedSubCommandValues.Write(writer);
        PostFixes.Write(writer);
        EnumData.Write(writer);

        writer.WriteUnsignedVarInt(SubCommandData.Length);
        foreach (var sub in SubCommandData)
        {
            writer.WriteVarString(sub.Name);
            writer.WriteUnsignedVarInt(sub.Values.Length);
            foreach (var v in sub.Values)
            {
                writer.WriteUShort(v.Index, BinaryStream.Endianess.Little);
                writer.WriteUShort(v.Value, BinaryStream.Endianess.Little);
            }
        }

        writer.WriteUnsignedVarInt(CommandData.Length);
        foreach (var c in CommandData)
            c.Write(writer);

        writer.WriteUnsignedVarInt(SoftEnums.Length);
        foreach (var s in SoftEnums)
            s.Write(writer);

        writer.WriteUnsignedVarInt(Constraints.Length);
        foreach (var c in Constraints)
            c.Write(writer);

        return writer.GetBufferDisposing();
    }
}
