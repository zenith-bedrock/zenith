using Zenith.Server;

namespace Zenith.Gameplay.Commands.BuiltIn;

static class AboutCommand
{
    public static Command Create() => new(
        name: "about",
        description: "Shows software version and build info",
        execute: ctx =>
        {
            ctx.Sender.Session.Protocol.Command.SendCommandOutputSuccess(
                ctx.CommandText, ctx.Origin,
                $"§eZenith§f {ServerIdentity.ProductVersion} " +
                $"(protocol §b{ServerIdentity.ProtocolVersion}§f / §a{ServerIdentity.VersionName}§f) " +
                $"§7[{ServerIdentity.GitCommit}]");
        });
}
