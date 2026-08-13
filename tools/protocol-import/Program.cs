using Spectre.Console.Cli;
using Zenith.ProtocolImport.Commands;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("protocol-import");

    config.AddCommand<PullCommand>("pull")
        .WithDescription("Download one verified schema snapshot into the local cache.");

    config.AddCommand<ScaffoldCommand>("scaffold")
        .WithDescription("Scaffold a GamePacket-attributed class from a cached packet schema.");

    config.AddCommand<DiffCommand>("diff")
        .WithDescription("Compare an already-migrated generated packet against a freshly cached schema.");

    config.AddCommand<ListCommand>("list")
        .WithDescription("Show which packets are cached and/or migrated.");

    config.AddCommand<ReportCommand>("report")
        .WithDescription("Show cache provenance, codegen coverage, and unsupported schema constructs.");

    config.AddCommand<CoverageCommand>("coverage")
        .WithDescription("Show cached schema coverage, generated/manual packets, and unsupported reasons.");

    config.AddCommand<CompatibilityCommand>("compatibility")
        .WithDescription("Report protocol compatibility evidence; use --json in CI.");

    config.AddCommand<ValidateCommand>("validate")
        .WithDescription("Fail when the verified snapshot or generated packet contract is stale.");

    config.AddCommand<UpgradeCommand>("upgrade")
        .WithDescription("Produce a migration plan and optionally apply guarded GREEN additions.");

    config.AddCommand<ReconcileCommand>("reconcile")
        .WithDescription("Reconcile toward any cached target snapshot, including a downgrade, with guarded codegen only.");

    config.AddCommand<ReconcileCommand>("downgrade")
        .WithDescription("Alias for reconcile; set --from newer snapshot and --to older target snapshot.");
});

return app.Run(args);
