using Zenith.Gameplay.Commands;
using Zenith.Diagnostics;
using Zenith.Player;
using Xunit;

namespace Zenith.Tests;

public sealed class CommandCatalogTests
{
    [Fact]
    public void Parse_alias_enum_optional_and_overload_returns_typed_values()
    {
        var catalog = CreateCatalog();

        var result = catalog.Parse("/gm creative Alice", CommandPermission.Any);

        Assert.Equal(CommandParseStatus.Success, result.Status);
        Assert.Equal("gamemode", result.Definition!.Name);
        Assert.Equal(GameMode.Creative, result.Arguments!["mode"]);
        Assert.Equal("Alice", result.Arguments["target"]);
    }

    [Fact]
    public void Parse_range_and_invalid_enum_return_consistent_statuses()
    {
        var catalog = CreateCatalog();

        Assert.Equal(CommandParseStatus.Success, catalog.Parse("/sample 20", CommandPermission.Any).Status);
        Assert.Equal(CommandParseStatus.Invalid, catalog.Parse("/sample 21", CommandPermission.Any).Status);
        Assert.Equal(CommandParseStatus.Invalid, catalog.Parse("/gm adventure", CommandPermission.Any).Status);
    }

    [Fact]
    public void Parse_denies_operator_command_and_unknown_command_is_distinct()
    {
        var catalog = CreateCatalog();

        Assert.Equal(CommandParseStatus.Denied, catalog.Parse("/stop", CommandPermission.Any).Status);
        Assert.Equal(CommandParseStatus.Unknown, catalog.Parse("/missing", CommandPermission.Operator).Status);
    }

    [Fact]
    public void Suggestions_include_canonical_names_and_aliases()
    {
        var catalog = CreateCatalog();

        Assert.Equal(["gamemode", "gm"], catalog.Suggest("g"));
    }

    [Fact]
    public void Diagnostics_command_is_exposed_as_a_concrete_command()
    {
        var runtime = new CommandRuntime(new PlayerManager(), CreateDiagnostics());

        Assert.Equal(CommandParseStatus.Success, runtime.Catalog.Parse("/diagnostics snapshot", CommandPermission.Any).Status);
        Assert.Equal(CommandParseStatus.Invalid, runtime.Catalog.Parse("/diagnostics missing", CommandPermission.Any).Status);
    }

    [Fact]
    public void Diagnostics_command_capturesBaselineAndComparesWithoutGameplayMutation()
    {
        var fixture = new IntentTestFixture();
        var player = fixture.AddInGamePlayer("operator");

        var snapshot = fixture.Context.Commands.Execute(player, "/diagnostics snapshot");
        var comparison = fixture.Context.Commands.Execute(player, "/diagnostics compare");

        Assert.Contains("Baseline captured", snapshot.Message);
        Assert.Contains("diagnostics compare", comparison.Message);
        Assert.True(player.IsInGame);
    }

    private static DiagnosticsInvestigation CreateDiagnostics()
    {
        var builder = new DiagnosticsBuilder();
        var runtime = builder.Build();
        return new DiagnosticsInvestigation(runtime, new DiagnosticsIncidentBuffer(runtime));
    }

    private static CommandCatalog CreateCatalog()
    {
        var modes = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["survival"] = GameMode.Survival, ["creative"] = GameMode.Creative
        };
        var catalog = new CommandCatalog();
        catalog.Register(new CommandDefinition("gamemode", "Set game mode", CommandPermission.Any,
            [new CommandOverload(new EnumCommandArgument("mode", modes), new PlayerCommandArgument("target", name => name, Optional: true))], ["gm"]));
        catalog.Register(new CommandDefinition("sample", "Range proof", CommandPermission.Any,
            new CommandOverload(new IntegerCommandArgument("ticks", 1, 20))));
        catalog.Register(new CommandDefinition("stop", "Permission proof", CommandPermission.Operator,
            new CommandOverload()));
        return catalog;
    }
}
