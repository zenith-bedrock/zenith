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

    private static CommandDefinition Def(string name, params string[] aliases) =>
        new(name, "test", CommandPermission.Any, [new CommandOverload()], aliases);

    private static void AssertUnchanged(CommandCatalog catalog, int definitions, params string[] unknown)
    {
        Assert.Equal(definitions, catalog.Definitions.Count);
        foreach (var name in unknown)
            Assert.Equal(CommandParseStatus.Unknown, catalog.Parse("/" + name, CommandPermission.Operator).Status);
    }

    [Theory]
    [InlineData("a", "b", "b")]   // alias repeated inside the definition
    [InlineData("a", "a")]        // alias equal to the name
    [InlineData("a", "A")]        // same, ignoring case
    [InlineData("a", "")]         // empty alias
    [InlineData("a", "x y")]      // alias with whitespace
    public void A_definition_with_bad_names_is_rejected_and_leaves_the_catalog_untouched(string name, params string[] aliases)
    {
        var catalog = new CommandCatalog();
        catalog.Register(Def("existing", "ex"));

        Assert.ThrowsAny<Exception>(() => catalog.Register(Def(name, aliases)));

        AssertUnchanged(catalog, 1, name, aliases.FirstOrDefault(a => a.Length > 0 && !a.Contains(' ')) ?? "zzz");
        Assert.Equal(CommandParseStatus.Success, catalog.Parse("/ex", CommandPermission.Any).Status);
    }

    [Fact]
    public void A_collision_with_an_existing_alias_registers_nothing_of_the_new_definition()
    {
        var catalog = new CommandCatalog();
        catalog.Register(Def("existing", "ex"));

        Assert.Throws<InvalidOperationException>(() => catalog.Register(Def("fresh", "fr", "ex")));

        AssertUnchanged(catalog, 1, "fresh", "fr");
        catalog.Register(Def("fresh", "fr")); // the names are still free
        Assert.Equal(2, catalog.Definitions.Count);
    }

    [Fact]
    public void A_frozen_catalog_refuses_registration_and_keeps_its_definitions()
    {
        var catalog = new CommandCatalog();
        catalog.Register(Def("one"));
        catalog.Freeze();

        Assert.Throws<InvalidOperationException>(() => catalog.Register(Def("two")));

        AssertUnchanged(catalog, 1, "two");
    }

    [Fact]
    public void The_catalog_keeps_its_own_copy_of_the_definitions_lists()
    {
        var aliases = new List<string> { "o" };
        var catalog = new CommandCatalog();
        catalog.Register(new CommandDefinition("one", "t", CommandPermission.Any, [new CommandOverload()], aliases));

        aliases.Add("sneaky");

        Assert.Equal(CommandParseStatus.Unknown, catalog.Parse("/sneaky", CommandPermission.Operator).Status);
        Assert.Single(catalog.Definitions[0].Aliases);
    }
}
