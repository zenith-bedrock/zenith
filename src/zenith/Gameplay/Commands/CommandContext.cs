using Zenith.Packets;

namespace Zenith.Gameplay.Commands;

sealed class CommandContext
{
    public Player.Player Sender { get; }
    public string CommandText { get; }
    public string[] Args { get; }
    public CommandOriginData Origin { get; }

    public CommandContext(Player.Player sender, string commandText, string[] args, CommandOriginData origin)
    {
        Sender = sender;
        CommandText = commandText;
        Args = args;
        Origin = origin;
    }
}
