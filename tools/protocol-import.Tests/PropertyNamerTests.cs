using Xunit;
using Zenith.ProtocolImport.Scaffolding;

namespace Zenith.ProtocolImport.Tests;

public class PropertyNamerTests
{
    [Theory]
    [InlineData("Target Actor Runtime ID", "TargetActorRuntimeId")]
    [InlineData("Swing Source", "SwingSource")]
    [InlineData("IsInternal", "IsInternal")]
    [InlineData("On Ground", "OnGround")]
    [InlineData("UUID", "Uuid")]
    [InlineData("Y-Head Rotation", "YHeadRotation")]
    public void Converts_to_pascal_case(string raw, string expected)
    {
        Assert.Equal(expected, PropertyNamer.ToPascalCase(raw));
    }
}
