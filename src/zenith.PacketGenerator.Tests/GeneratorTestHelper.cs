using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Zenith.PacketGenerator;

namespace Zenith.PacketGenerator.Tests;

/// <summary>
/// Minimal stand-ins for BinaryStream/DataPacket/WireAttributes, matching the real
/// namespaces/signatures so the generator's ForAttributeWithMetadataName matching and
/// symbol-shape probing (Read/Write/Decode) behave identically to the real assembly,
/// without pulling in the whole zenith project as a test dependency.
/// </summary>
internal static class GeneratorTestHelper
{
    private const string Preamble = """
        namespace Zenith.Raknet.Stream
        {
            public ref struct BinaryStream
            {
                public enum Endianess : byte { Big, Little }
                public bool ReadBool() => default;
                public void WriteBool(bool v) { }
                public byte ReadByte() => default;
                public void WriteByte(byte v) { }
                public short ReadShort(Endianess e = Endianess.Big) => default;
                public void WriteShort(short v, Endianess e = Endianess.Big) { }
                public ushort ReadUShort(Endianess e = Endianess.Big) => default;
                public void WriteUShort(ushort v, Endianess e = Endianess.Big) { }
                public int ReadInt(Endianess e = Endianess.Big) => default;
                public void WriteInt(int v, Endianess e = Endianess.Big) { }
                public uint ReadUInt(Endianess e = Endianess.Big) => default;
                public void WriteUInt(uint v, Endianess e = Endianess.Big) { }
                public long ReadLong(Endianess e = Endianess.Big) => default;
                public void WriteLong(long v, Endianess e = Endianess.Big) { }
                public ulong ReadULong(Endianess e = Endianess.Big) => default;
                public void WriteULong(ulong v, Endianess e = Endianess.Big) { }
                public float ReadFloat(Endianess e = Endianess.Big) => default;
                public void WriteFloat(float v, Endianess e = Endianess.Big) { }
                public double ReadDouble(Endianess e = Endianess.Big) => default;
                public void WriteDouble(double v, Endianess e = Endianess.Big) { }
                public int ReadUnsignedVarInt() => default;
                public void WriteUnsignedVarInt(int v) { }
                public long ReadUnsignedVarLong() => default;
                public void WriteUnsignedVarLong(long v) { }
                public int ReadVarInt() => default;
                public void WriteVarInt(int v) { }
                public long ReadVarLong() => default;
                public void WriteVarLong(long v) { }
                public string ReadString() => "";
                public void WriteString(string v) { }
                public string ReadVarString() => "";
                public void WriteVarString(string v) { }
                public byte[] ReadByteArray() => System.Array.Empty<byte>();
                public void WriteByteArray(System.ReadOnlySpan<byte> v) { }
                public System.Guid ReadUuid() => default;
                public void WriteUuid(System.Guid v) { }
                public System.Span<byte> GetBufferDisposing() => default;
            }
        }

        namespace Zenith.Packets
        {
            public abstract class DataPacket
            {
                public abstract int Id { get; }
                public virtual System.Span<byte> Encode() => System.Array.Empty<byte>();
                public abstract void Decode(ref Zenith.Raknet.Stream.BinaryStream stream);
            }
        }

        namespace Zenith.Packets.Generation
        {
            using Zenith.Raknet.Stream;

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class GamePacketAttribute(int protocolId) : System.Attribute
            {
                public int ProtocolId { get; } = protocolId;
            }

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class WireAttribute(BinaryStream.Endianess endianess = BinaryStream.Endianess.Big) : System.Attribute
            {
                public BinaryStream.Endianess Endianess { get; } = endianess;
            }

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class WireVarAttribute(bool unsigned = false) : System.Attribute
            {
                public bool Unsigned { get; } = unsigned;
            }

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class WireStringAttribute(bool utf16LengthPrefixed = false) : System.Attribute
            {
                public bool Utf16LengthPrefixed { get; } = utf16LengthPrefixed;
            }

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class WireByteArrayAttribute : System.Attribute;

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class WireUuidAttribute : System.Attribute;

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class WireUuidArrayAttribute : System.Attribute;

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class WireNestedAttribute : System.Attribute;

            public enum CountEncoding { UnsignedVarInt, FixedByte, FixedUShort, FixedUInt }

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class WireNestedArrayAttribute(
                CountEncoding countEncoding = CountEncoding.UnsignedVarInt,
                BinaryStream.Endianess countEndianess = BinaryStream.Endianess.Little) : System.Attribute
            {
                public CountEncoding CountEncoding { get; } = countEncoding;
                public BinaryStream.Endianess CountEndianess { get; } = countEndianess;
            }

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class WireOptionalAttribute : System.Attribute;

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class WireWhenAttribute(string otherProperty, object value) : System.Attribute
            {
                public string OtherProperty { get; } = otherProperty;
                public object Value { get; } = value;
            }

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class WireIgnoreAttribute : System.Attribute;
        }
        """;

    public static (string[] GeneratedSources, ImmutableArray<Diagnostic> Diagnostics) Run(string source)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(Preamble), CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var generator = new GamePacketGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGeneratorsAndUpdateCompilation(
            compilation, out _, out var diagnostics);

        var runResult = driver.GetRunResult();
        var generatedSources = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString())
            .ToArray();

        return (generatedSources, diagnostics.AddRange(runResult.Diagnostics));
    }
}
