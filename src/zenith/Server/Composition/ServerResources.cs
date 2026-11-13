using System.Diagnostics;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Inventory;
using Zenith.Logging;
using Zenith.World;

namespace Zenith.Server.Composition;

/// <summary>
/// Required catalogs prepared before storage, gameplay or transports start. This is internal boot composition,
/// not an extension registry. Keep dependencies explicit and validation with the resource that owns it.
/// </summary>
sealed record ServerResources(
    BlockPalette Blocks,
    ItemPalette Items,
    RecipeRegistry Recipes,
    ItemDurability Durability,
    CreativeCatalog Creative)
{
    public static ServerResources Load(ILogger logger)
    {
        var blocks = LoadStage("block palette", () =>
        {
            var palette = BlockPaletteLoader.FromEmbeddedResource();
            global::Zenith.World.Blocks.Load(palette);
            return palette;
        });
        var items = LoadStage("item palette and block tables", () =>
        {
            var palette = ItemPaletteLoader.FromEmbeddedResource();
            string[] required = ["air", "stone", "grass_block", "dirt", "oak_planks", "oak_log", "oak_leaves",
                "sand", "gravel", "bedrock", "water", "cobblestone", "deepslate", "chest"];
            foreach (var name in required) palette.Require($"minecraft:{name}");
            Tools.Load(palette);
            DigProfiles.EnsureLoaded();
            BlockLoot.Load(palette);
            return palette;
        });
        var recipes = LoadStage("recipes", () => RecipeRegistry.CreateDefault(items));
        var durability = LoadStage("item durability", () => ItemDurability.CreateDefault(items));
        var creative = LoadStage("creative catalog", () =>
        {
            var catalog = CreativeCatalog.CreateDefault(items);
            catalog.Freeze();
            return catalog;
        });
        // Species constructors also use these definitions. Validate every row, including species not spawned yet.
        LoadStage("mob definitions", () =>
        {
            MobDefinitions.ValidateLoot(items);
            return true;
        });
        return new ServerResources(blocks, items, recipes, durability, creative);

        T LoadStage<T>(string name, Func<T> load)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                var resource = load();
                logger.Info($"startup: {name} ready in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms");
                return resource;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Startup resource '{name}' failed: {ex.Message}", ex);
            }
        }
    }
}
