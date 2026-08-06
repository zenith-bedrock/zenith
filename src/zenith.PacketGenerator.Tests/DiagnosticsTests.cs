using Xunit;

namespace Zenith.PacketGenerator.Tests;

public class DiagnosticsTests
{
    [Fact]
    public void Not_partial_reports_ZPG001()
    {
        const string source = """
            using Zenith.Packets.Generation;
            namespace Zenith.Packets;

            [GamePacket(1)]
            sealed class NotPartialPacket : DataPacket
            {
                [WireVar]
                public int X { get; set; }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.Run(source);
        Assert.Contains(diagnostics, d => d.Id == "ZPG001");
    }

    [Fact]
    public void Non_data_packet_reports_ZPG002()
    {
        const string source = """
            using Zenith.Packets.Generation;
            namespace Zenith.Packets;

            [GamePacket(1)]
            sealed partial class NotAPacket
            {
                [WireVar]
                public int X { get; set; }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.Run(source);
        Assert.Contains(diagnostics, d => d.Id == "ZPG002");
    }

    [Fact]
    public void Hand_written_encode_reports_ZPG003()
    {
        const string source = """
            using Zenith.Packets.Generation;
            using Zenith.Raknet.Stream;
            namespace Zenith.Packets;

            [GamePacket(1)]
            sealed partial class ConflictPacket : DataPacket
            {
                [WireVar]
                public int X { get; set; }

                public override System.Span<byte> Encode() => default;
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.Run(source);
        Assert.Contains(diagnostics, d => d.Id == "ZPG003");
    }

    [Fact]
    public void Wrong_clr_type_reports_ZPG004()
    {
        const string source = """
            using Zenith.Packets.Generation;
            namespace Zenith.Packets;

            [GamePacket(1)]
            sealed partial class BadTypePacket : DataPacket
            {
                [WireUuid]
                public string NotAGuid { get; set; } = "";
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.Run(source);
        Assert.Contains(diagnostics, d => d.Id == "ZPG004");
    }

    [Fact]
    public void Multiple_base_attributes_reports_ZPG005()
    {
        const string source = """
            using Zenith.Packets.Generation;
            namespace Zenith.Packets;

            [GamePacket(1)]
            sealed partial class MultiAttrPacket : DataPacket
            {
                [WireVar]
                [WireString]
                public string X { get; set; } = "";
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.Run(source);
        Assert.Contains(diagnostics, d => d.Id == "ZPG005");
    }

    [Fact]
    public void Optional_on_non_nullable_reports_ZPG008()
    {
        const string source = """
            using Zenith.Packets.Generation;
            namespace Zenith.Packets;

            [GamePacket(1)]
            sealed partial class NonNullableOptionalPacket : DataPacket
            {
                [WireVar]
                [WireOptional]
                public int X { get; set; }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.Run(source);
        Assert.Contains(diagnostics, d => d.Id == "ZPG008");
    }

    [Fact]
    public void When_referencing_unknown_property_reports_ZPG009()
    {
        const string source = """
            using Zenith.Packets.Generation;
            namespace Zenith.Packets;

            [GamePacket(1)]
            sealed partial class BadWhenPacket : DataPacket
            {
                [WireVar]
                [WireWhen("DoesNotExist", 1)]
                public int X { get; set; }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.Run(source);
        Assert.Contains(diagnostics, d => d.Id == "ZPG009");
    }

    [Fact]
    public void When_referencing_later_property_reports_ZPG010()
    {
        const string source = """
            using Zenith.Packets.Generation;
            namespace Zenith.Packets;

            [GamePacket(1)]
            sealed partial class OutOfOrderPacket : DataPacket
            {
                [WireVar]
                [WireWhen("Mode", 1)]
                public int X { get; set; }

                [WireVar]
                public int Mode { get; set; }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.Run(source);
        Assert.Contains(diagnostics, d => d.Id == "ZPG010");
    }

    [Fact]
    public void Duplicate_protocol_id_reports_ZPG013()
    {
        const string source = """
            using Zenith.Packets.Generation;
            namespace Zenith.Packets;

            [GamePacket(42)]
            sealed partial class FirstPacket : DataPacket
            {
                [WireVar]
                public int X { get; set; }
            }

            [GamePacket(42)]
            sealed partial class SecondPacket : DataPacket
            {
                [WireVar]
                public int Y { get; set; }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.Run(source);
        Assert.Contains(diagnostics, d => d.Id == "ZPG013");
    }
}
