using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Raknet.Stream;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Inventory;

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

    /// <summary>
    /// Regression for ADR §139: every recipe used to default to "crafting_table" on the wire
    /// (ShapelessCraftingRecipe's own field default), including ones craftable in the personal 2×2
    /// grid. The Block field must now reflect each recipe's actual RequiresTable value.
    /// </summary>
    [Fact]
    public void BuildCraftingData_sets_Block_per_recipe_RequiresTable()
    {
        var registry = RecipeRegistry.CreateDefault();
        var palette = ItemPaletteLoader.FromEmbeddedResource();
        var packet = InventoryProtocol.BuildCraftingData(registry, palette);

        var logToPlanks = Array.Find(packet.Recipes, r => r.RecipeNetworkId == RecipeRegistry.OakLogToPlanks);
        Assert.NotNull(logToPlanks);
        Assert.Equal("", logToPlanks!.Block);

        var planksToChest = Array.Find(packet.Recipes, r => r.RecipeNetworkId == RecipeRegistry.OakPlanksToChest);
        Assert.NotNull(planksToChest);
        Assert.Equal("crafting_table", planksToChest!.Block);

        var pickaxe = Array.Find(packet.Recipes, r => r.RecipeNetworkId == RecipeRegistry.WoodenPickaxe);
        Assert.NotNull(pickaxe);
        Assert.Equal("crafting_table", pickaxe!.Block);
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
        Assert.Equal(0, stream.ReadUnsignedVarInt()); // shaped_recipes — protocol 2168+ shape (ADR §86)
        Assert.Equal(registry.SnapshotRecipes().Count, stream.ReadUnsignedVarInt()); // shapeless_recipes

        // Skip recipe payloads until the trailing empty-array run + ClearRecipes by scanning for
        // ending: after recipes, protocol 2168+ has 9 more empty lists (was 3) + bool ClearRecipes.
        // Validate trailing ClearRecipes=true and that net ids encode into payload.
        var textish = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("zenith:recipe_1", textish);
        Assert.Contains("zenith:recipe_2", textish);
        Assert.Contains("crafting_table", textish);
        Assert.Contains("name", textish); // ItemDescriptor kind label (Cereal map-style encoding)

        Assert.True(bytes[^1] == 1); // ClearRecipes = true (WriteBool at end)
    }

    [Fact]
    public void ShapelessRecipe_encode_shape_matches_protocol_2168_layout()
    {
        // Byte-level decode of one recipe entry (ADR §86): descriptor variant + kind label +
        // item name + metadata + count per input, no per-entry type tag, Optional
        // UnlockingRequirement has-flag=false, unsigned net id.
        var recipe = new ShapelessCraftingRecipe
        {
            RecipeId = "zenith:recipe_1",
            Inputs = [new DefaultDescriptorInput("minecraft:oak_log", 0, 1)],
            Outputs = [new NetworkItemStack(5, 4, 0)],
            Block = "crafting_table",
            Priority = 0,
            RecipeNetworkId = 1
        };

        var writer = new BinaryStream();
        recipe.Write(ref writer);
        var bytes = writer.GetBufferDisposing().ToArray();
        var stream = new BinaryStream(bytes);

        Assert.Equal("zenith:recipe_1", stream.ReadVarString());
        Assert.Equal(1, stream.ReadUnsignedVarInt()); // inputs count

        Assert.Equal(1, stream.ReadUnsignedVarInt()); // ItemDescriptor variant = Default
        Assert.Equal("name", stream.ReadVarString()); // descriptor-kind label
        Assert.Equal("minecraft:oak_log", stream.ReadVarString());
        Assert.Equal(0, stream.ReadVarInt()); // metadata
        Assert.Equal(1, stream.ReadVarInt()); // ingredient count

        Assert.Equal(1, stream.ReadUnsignedVarInt()); // outputs count
        Assert.Equal(5, stream.ReadVarInt()); // ItemStack (WriteItemStack) network id — VarInt, not fixed short
        Assert.Equal((ushort)4, stream.ReadUShort(BinaryStream.Endianess.Little));
        Assert.Equal(0, (int)stream.ReadUnsignedVarInt()); // meta
        Assert.Equal(0, stream.ReadVarInt()); // block_runtime_id
        Assert.Equal(10, (int)stream.ReadUnsignedVarInt()); // extra blob length (has_nbt + can_place + can_destroy)
        for (var i = 0; i < 10; i++) Assert.Equal(0, stream.ReadByte());

        Assert.Equal(Guid.Empty, stream.ReadUuid());
        Assert.Equal("crafting_table", stream.ReadVarString());
        Assert.Equal(0, stream.ReadVarInt()); // priority
        Assert.False(stream.ReadBool()); // UnlockingRequirement Optional has-flag
        Assert.Equal(1, stream.ReadUnsignedVarInt()); // recipe net id
        Assert.True(stream.IsEndOfFile);
    }
}
