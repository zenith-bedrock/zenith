using Spectre.Console.Cli;
using Zenith.ProtocolImport.Commands;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("protocol-import");

    config.AddCommand<PullCommand>("pull")
        .WithDescription("Download one verified schema snapshot into the local cache.");

    config.AddCommand<ScaffoldCommand>("scaffold")
        .WithDescription("Scaffold a [GamePacket]-attributed class from a cached packet schema.");

    config.AddCommand<DiffCommand>("diff")
        .WithDescription("Compare an already-migrated packet's attributes against a freshly cached schema.");

    config.AddCommand<ListCommand>("list")
        .WithDescription("Show which packets are cached and/or migrated.");

    config.AddCommand<ReportCommand>("report")
        .WithDescription("Show cache provenance, codegen coverage, and unsupported schema constructs.");

    config.AddCommand<UpgradeCommand>("upgrade")
        .WithDescription("Produce a read-only migration plan between two verified snapshots.");
});

return app.Run(args);
