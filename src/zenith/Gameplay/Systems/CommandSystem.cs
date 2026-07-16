using Zenith.Gameplay.Commands;
using Zenith.Gameplay.Runtime;
using Zenith.Player;

namespace Zenith.Gameplay.Systems;

sealed class CommandSystem : IGameSystem
{
    private readonly PlayerManager _players;
    private readonly CommandPalette _palette;

    public CommandSystem(PlayerManager players, CommandPalette palette)
    {
        _players = players;
        _palette = palette;
    }

    public CommandPalette Palette => _palette;

    public void Tick(GameClock clock)
    {
        _ = clock;
        if (_players.Count == 0) return;

        foreach (var player in _players.Online)
        {
            if (!player.TryConsumeCommand(out var text, out var origin)) continue;

            var args = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var name = args.Length > 0 ? args[0] : "";
            var cmdArgs = args.Length > 1 ? args[1..] : [];

            var command = _palette.Get(name);
            if (command is null)
            {
                player.Session.Protocol.Command.SendCommandOutputError(
                    text, origin, "commands.generic.notFound");
                continue;
            }

            try
            {
                var ctx = new CommandContext(player, text, cmdArgs, origin);
                command.Execute(ctx);
            }
            catch (Exception ex)
            {
                player.Session.Context.Logger.Error(
                    $"Command /{command.Name} by {player.Username} threw: {ex.Message}");
                player.Session.Protocol.Command.SendCommandOutputError(
                    text, origin, "commands.generic.exception");
            }
        }
    }
}
