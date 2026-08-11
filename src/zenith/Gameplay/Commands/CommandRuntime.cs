using Zenith.Player;

namespace Zenith.Gameplay.Commands;

/// <summary>Gameplay-facing command use cases. Parsing is protocol-free; mutations publish existing intents.</summary>
sealed class CommandRuntime
{
    private readonly PlayerManager _players;
    private readonly CommandCatalog _catalog = new();

    public CommandRuntime(PlayerManager players)
    {
        _players = players;
        var modes = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["survival"] = GameMode.Survival, ["s"] = GameMode.Survival, ["0"] = GameMode.Survival,
            ["creative"] = GameMode.Creative, ["c"] = GameMode.Creative, ["1"] = GameMode.Creative
        };
        _catalog.Register(new CommandDefinition("gamemode", "Change game mode", CommandPermission.Any,
            [new CommandOverload(new EnumCommandArgument("mode", modes), new PlayerCommandArgument("target", name => _players.Get(name), Optional: true))], ["gm"]));
        _catalog.Register(new CommandDefinition("help", "List available commands", CommandPermission.Any,
            new CommandOverload(), new CommandOverload(new StringCommandArgument("command"))));
        _catalog.Register(new CommandDefinition("sample", "Show a bounded runtime sample request", CommandPermission.Any,
            new CommandOverload(), new CommandOverload(new IntegerCommandArgument("ticks", 1, 20))));
    }

    public IReadOnlyList<string> Suggest(string prefix) => _catalog.Suggest(prefix);
    internal CommandCatalog Catalog => _catalog;

    public CommandFeedback Execute(Player.Player source, string line)
    {
        var parsed = _catalog.Parse(line, CommandPermission.Any);
        if (parsed.Status == CommandParseStatus.Unknown) return new("Command", "Unknown command.");
        if (parsed.Status == CommandParseStatus.Denied) return new("Command", "You do not have permission.");
        if (parsed.Status == CommandParseStatus.Invalid) return new("Command", $"Usage: /{parsed.Definition!.Name}");

        var arguments = parsed.Arguments!;
        switch (parsed.Definition!.Name)
        {
            case "gamemode":
                var target = arguments.TryGetValue("target", out var targetValue) ? (Player.Player)targetValue : source;
                target.SubmitGameMode((GameMode)arguments["mode"]);
                return new("Game mode", $"Queued for {target.Username}.");
            case "help":
                return new("Commands", string.Join(", ", _catalog.Suggest("")));
            case "sample":
                return new("Command", arguments.TryGetValue("ticks", out var ticks) ? $"Sample request: {ticks} ticks." : "Sample request: default.");
            default:
                return new("Command", "Unknown command.");
        }
    }
}

readonly record struct CommandFeedback(string Title, string Message);
