using System.Text.Json;
using Xunit;
using Zenith.ProtocolImport.Commands;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Tests;

public sealed class ProtocolGovernanceJsonTests
{
    [Fact]
    public void Compatibility_json_keeps_the_documented_machine_contract()
    {
        var report = new ProtocolGovernance("1.26.50", 0.85, 17, 3,
            [new UnsupportedConstruct("Manual", "Choice", "union")],
            [new UnsupportedConstruct("Generated", "Values", "array")], [], []);

        using var document = JsonDocument.Parse(ProtocolGovernanceJson.Compatibility(report));
        var root = document.RootElement;
        Assert.Equal("1.26.50", root.GetProperty("protocol").GetString());
        Assert.Equal(JsonValueKind.Number, root.GetProperty("coverage").ValueKind);
        Assert.Equal(17, root.GetProperty("generated").GetInt32());
        Assert.Equal(3, root.GetProperty("manual").GetInt32());
        Assert.Equal(1, root.GetProperty("red").GetInt32());
        Assert.Equal(1, root.GetProperty("yellow").GetInt32());
        Assert.False(root.GetProperty("generatedOutOfDate").GetBoolean());
        Assert.Equal("Manual", root.GetProperty("redItems")[0].GetProperty("packet").GetString());
        Assert.Equal("Generated", root.GetProperty("yellowItems")[0].GetProperty("packet").GetString());
    }

    [Fact]
    public void Validation_json_reports_a_valid_synchronized_snapshot()
    {
        var report = new ProtocolGovernance("1.26.50", 1, 1, 0, [], [], [], []);

        using var document = JsonDocument.Parse(ProtocolGovernanceJson.Validation(report, valid: true));
        Assert.True(document.RootElement.GetProperty("valid").GetBoolean());
        Assert.False(document.RootElement.GetProperty("generatedOutOfDate").GetBoolean());
    }
}
