using Zenith.Gameplay;
using Zenith.Network.Packets;
using Zenith.Network.Protocol;
using Zenith.Raknet.Stream;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class CraftingDataPacketTests
{
    public CraftingDataPacketTests()
    {
        Blocks.ResetForTests();
        Blocks.Load(BlockPaletteLoader.FromEmbeddedResource());
    }

    [Fact]
    public void BuildCraftingData_matches_registry_net_ids_and_clear_flag()
    {
        var registry = RecipeRegistry.CreateDefault();
        var palette = ItemPaletteLoader.FromEmbeddedResource();
        var packet = InventoryProtocol.BuildCraftingData(registry, palette);

        Assert.True(packet.ClearRecipes);
        Assert.Equal(registry.SnapshotRecipes().Count, packet.Recipes.Length);
        Assert.Equal(RecipeRegistry.OakLogToPlanks, packet.Recipes[0].RecipeNetworkId);
        Assert.Equal(RecipeRegistry.OakPlanksToChest, packet.Recipes[1].RecipeNetworkId);
    }

    [Fact]
    public void CraftingData_encode_shape_recipe_count_clear_and_net_ids()
    {
        var registry = RecipeRegistry.CreateDefault();
        var palette = ItemPaletteLoader.FromEmbeddedResource();
        var packet = InventoryProtocol.BuildCraftingData(registry, palette);
        var bytes = packet.Encode().ToArray();

        var stream = new BinaryStream(bytes);
        Assert.Equal((int)ProtocolInfo.CRAFTING_DATA_PACKET, stream.ReadUnsignedVarInt());
        Assert.Equal(2, stream.ReadUnsignedVarInt()); // shapeless recipes

        // Skip recipe payloads until potions/reducers/ClearRecipes by scanning for ending:
        // after recipes: 3× empty lists + bool ClearRecipes.
        // Validate trailing ClearRecipes=true and that net ids encode into payload.
        var textish = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("zenith:recipe_1", textish);
        Assert.Contains("zenith:recipe_2", textish);
        Assert.Contains("crafting_table", textish);

        Assert.True(bytes[^1] == 1); // ClearRecipes = true (WriteBool at end)
    }
}
