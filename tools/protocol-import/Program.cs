using Spectre.Console.Cli;
using Zenith.ProtocolImport.Commands;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("protocol-import");

    config.AddCommand<PullCommand>("pull")
        .WithDescription("Download packets/types/enums JSON from EndstoneMC/protocol-docs into a local cache.");

    config.AddCommand<ScaffoldCommand>("scaffold")
        .WithDescription("Scaffold a [GamePacket]-attributed class from a cached packet schema.");

    config.AddCommand<DiffCommand>("diff")
        .WithDescription("Compare an already-migrated packet's attributes against a freshly cached schema.");

    config.AddCommand<ListCommand>("list")
        .WithDescription("Show which packets are cached and/or migrated.");
});

return app.Run(args);
