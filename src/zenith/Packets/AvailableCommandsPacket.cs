using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>Bedrock command metadata (AvailableCommands 0x4c). Built by the Session adapter.</summary>
sealed class AvailableCommandsPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.AVAILABLE_COMMANDS_PACKET;
    public List<string> EnumValues { get; } = [];
    public List<AvailableCommandEnum> Enums { get; } = [];
    public List<AvailableCommand> Commands { get; } = [];
    public override Span<byte> Encode()
    {
        var writer = new BinaryStream(); writer.WriteUnsignedVarInt(Id);
        WriteStrings(ref writer, EnumValues); writer.WriteUnsignedVarInt(0); writer.WriteUnsignedVarInt(0);
        writer.WriteUnsignedVarInt(Enums.Count); foreach (var e in Enums) e.Write(ref writer);
        writer.WriteUnsignedVarInt(0); writer.WriteUnsignedVarInt(Commands.Count); foreach (var c in Commands) c.Write(ref writer);
        writer.WriteUnsignedVarInt(0); writer.WriteUnsignedVarInt(0); return writer.GetBufferDisposing();
    }
    public override void Decode(ref BinaryStream stream) => throw new NotSupportedException("Server-only metadata packet.");
    private static void WriteStrings(ref BinaryStream writer, IReadOnlyList<string> values) { writer.WriteUnsignedVarInt(values.Count); foreach (var value in values) writer.WriteVarString(value); }
}
readonly record struct AvailableCommandEnum(string Name, IReadOnlyList<int> ValueIndices) { public void Write(ref BinaryStream w) { w.WriteVarString(Name); w.WriteUnsignedVarInt(ValueIndices.Count); foreach (var i in ValueIndices) w.WriteUInt((uint)i, BinaryStream.Endianess.Little); } }
readonly record struct AvailableCommand(string Name, string Description, string Permission, uint AliasEnumIndex, IReadOnlyList<AvailableCommandOverload> Overloads) { public void Write(ref BinaryStream w) { w.WriteVarString(Name); w.WriteVarString(Description); w.WriteUShort(0, BinaryStream.Endianess.Little); w.WriteVarString(Permission); w.WriteUInt(AliasEnumIndex, BinaryStream.Endianess.Little); w.WriteUnsignedVarInt(0); w.WriteUnsignedVarInt(Overloads.Count); foreach (var o in Overloads) o.Write(ref w); } }
readonly record struct AvailableCommandOverload(IReadOnlyList<AvailableCommandParameter> Parameters) { public void Write(ref BinaryStream w) { w.WriteBool(false); w.WriteUnsignedVarInt(Parameters.Count); foreach (var p in Parameters) p.Write(ref w); } }
readonly record struct AvailableCommandParameter(string Name, uint Type, bool Optional) { public void Write(ref BinaryStream w) { w.WriteVarString(Name); w.WriteUInt(Type, BinaryStream.Endianess.Little); w.WriteBool(Optional); w.WriteByte(0); } }
