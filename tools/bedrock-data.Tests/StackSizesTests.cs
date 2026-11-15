using System.Text.Json.Nodes;
using Xunit;

namespace Zenith.BedrockData.Tests;

public class StackSizesTests
{
    [Fact]
    public void Only_exceptions_are_kept_by_name_independent_of_source_registry_ids()
    {
        var source = JsonNode.Parse("""[{"name":"stone","id":1,"stackSize":64},{"name":"bucket","id":2,"stackSize":16},{"name":"sword","id":9000,"stackSize":1}]""")!.AsArray();
        Assert.Equal(new[] { ("minecraft:bucket", 16), ("minecraft:sword", 1) },
            StackSizes.Build(source).Select(kv => (kv.Key, kv.Value)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public void Invalid_capacity_fails_instead_of_becoming_an_inert_fact(int size) =>
        Assert.Throws<InvalidDataException>(() => StackSizes.Build(
            new JsonArray(new JsonObject { ["name"] = "invalid", ["stackSize"] = size })));

    [Fact]
    public void Duplicate_default_sized_entries_are_also_rejected() =>
        Assert.Throws<InvalidDataException>(() => StackSizes.Build(JsonNode.Parse(
            """[{"name":"stone","stackSize":64},{"name":"stone","stackSize":64}]""")!.AsArray()));
}
