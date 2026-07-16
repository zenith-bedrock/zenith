namespace Zenith.Gameplay.Commands.BuiltIn;

static class HelpCommand
{
    public static Command Create(CommandPalette palette) => new(
        name: "help",
        description: "Shows available commands",
        execute: ctx =>
        {
            var all = palette.GetAll();
            var sb = new System.Text.StringBuilder("§eAvailable commands: §f");
            var first = true;
            foreach (var cmd in all)
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append('/');
                sb.Append(cmd.Name);
            }
            ctx.Sender.Session.Protocol.Command.SendCommandOutputSuccess(
                ctx.CommandText, ctx.Origin, sb.ToString());
        });
}
