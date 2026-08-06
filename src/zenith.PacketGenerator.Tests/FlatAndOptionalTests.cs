using Xunit;

namespace Zenith.PacketGenerator.Tests;

public class FlatAndOptionalTests
{
    [Fact]
    public void Flat_packet_generates_matching_encode_decode()
    {
        const string source = """
            using Zenith.Packets.Generation;
            namespace Zenith.Packets;

            [GamePacket(10)]
            sealed partial class SetTimePacket : DataPacket
            {
                [WireVar]
                public int Time { get; set; }
            }
            """;

        var (generated, diagnostics) = GeneratorTestHelper.Run(source);

        Assert.Empty(diagnostics);
        var text = Assert.Single(generated);
        Assert.Contains("public override int Id => 10;", text);
        Assert.Contains("writer.WriteVarInt(Time);", text);
        Assert.Contains("Time = stream.ReadVarInt();", text);
    }

    [Fact]
    public void Unsigned_var_int_forces_UnsignedVarInt_without_a_cast()
    {
        const string source = """
            using Zenith.Packets.Generation;
            namespace Zenith.Packets;

            [GamePacket(21)]
            sealed partial class SomePacket : DataPacket
            {
                [WireVar(unsigned: true)]
                public int Flags { get; set; }
            }
            """;

        var (generated, diagnostics) = GeneratorTestHelper.Run(source);

        Assert.Empty(diagnostics);
        var text = Assert.Single(generated);
        Assert.Contains("writer.WriteUnsignedVarInt(Flags);", text);
        Assert.Contains("Flags = stream.ReadUnsignedVarInt();", text);
        Assert.DoesNotContain("WriteVarInt(Flags)", text);
    }

    [Fact]
    public void Optional_field_generates_has_value_flag_pattern()
    {
        const string source = """
            using Zenith.Packets.Generation;
            namespace Zenith.Packets;

            [GamePacket(101)]
            sealed partial class ModalFormResponsePacket : DataPacket
            {
                [WireVar]
                public uint FormId { get; set; }

                [WireString]
                [WireOptional]
                public string? FormUiJson { get; set; }
            }
            """;

        var (generated, diagnostics) = GeneratorTestHelper.Run(source);

        Assert.Empty(diagnostics);
        var text = Assert.Single(generated);
        Assert.Contains("if (FormUiJson is not null)", text);
        Assert.Contains("writer.WriteBool(true);", text);
        Assert.Contains("writer.WriteBool(false);", text);
        Assert.Contains("FormUiJson = stream.ReadBool() ? (string?)(stream.ReadVarString()) : null;", text);
    }

    [Fact]
    public void Conditional_field_wraps_in_if_matching_other_property()
    {
        const string source = """
            using Zenith.Packets.Generation;
            namespace Zenith.Packets;

            [GamePacket(5)]
            sealed partial class DisconnectPacket : DataPacket
            {
                [Wire]
                public bool HideDisconnectionScreen { get; set; }

                [WireString]
                [WireWhen(nameof(HideDisconnectionScreen), false)]
                public string Message { get; set; } = "";
            }
            """;

        var (generated, diagnostics) = GeneratorTestHelper.Run(source);

        Assert.Empty(diagnostics);
        var text = Assert.Single(generated);
        Assert.Contains("if (HideDisconnectionScreen == false)", text);
        Assert.Contains("Message = stream.ReadVarString();", text);
    }
}
