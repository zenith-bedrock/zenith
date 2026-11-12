using Spectre.Console.Cli;
using Zenith.BedrockData.Commands;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("bedrock-data");

    config.AddCommand<RecipesCommand>("recipes")
        .WithDescription("Build recipes.json.gz from a stock BDS install's behaviour packs, oracle-verified against a CraftingData capture.");

    config.AddCommand<ImportCommand>("import")
        .WithDescription("Import a verified BDS/Endstone bundle into the embedded block/item/creative/biome assets.");
});

return app.Run(args);
