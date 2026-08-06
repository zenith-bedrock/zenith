using Xunit;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Tests;

public class KnownTypeMapTests
{
    [Theory]
    [InlineData("uint8", "Fixed", "byte")]
    [InlineData("float", "Fixed", "float")]
    [InlineData("bool", "Fixed", "bool")]
    [InlineData("varint32", "Var", "int")]
    [InlineData("uvarint32", "Var", "uint")]
    [InlineData("string", "String", "string")]
    [InlineData("mce::uuid", "Uuid", "Guid")]
    [InlineData("actorruntimeid", "Var", "ulong")]
    public void Resolves_known_types(string endstoneType, string expectedKind, string expectedClrType)
    {
        var emission = KnownTypeMap.Resolve(endstoneType);
        Assert.Equal(expectedKind, emission.Kind.ToString());
        Assert.Equal(expectedClrType, emission.ClrType);
    }

    [Fact]
    public void Case_insensitive_lookup()
    {
        Assert.Equal("Fixed", KnownTypeMap.Resolve("UINT8").Kind.ToString());
    }

    [Fact]
    public void Unknown_type_falls_through()
    {
        var emission = KnownTypeMap.Resolve("SomeCustomStruct");
        Assert.Equal("Unknown", emission.Kind.ToString());
    }
}
