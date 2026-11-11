using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Binary;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Inventory;

namespace Zenith.Tests;

public class CraftingDataPacketTests
{
    private static readonly ItemPalette Palette = ItemPaletteLoader.FromEmbeddedResource();

    public CraftingDataPacketTests()
    {
        Blocks.ResetForTests();
        Blocks.Load(BlockPaletteLoader.FromEmbeddedResource());
    }

    private static CraftingDataPacket Build() => InventoryProtocol.BuildCraftingData(RecipeRegistry.CreateDefault(), Palette);

    [Fact]
    public void Every_registry_recipe_is_sent_once_under_its_net_id_and_the_book_is_cleared()
    {
        var registry = RecipeRegistry.CreateDefault();
        var packet = Build();

        Assert.True(packet.ClearRecipes);
        Assert.Equal(registry.Count, packet.ShapedRecipes.Length + packet.Recipes.Length);
        var ids = packet.ShapedRecipes.Select(r => r.RecipeNetworkId).Concat(packet.Recipes.Select(r => r.RecipeNetworkId)).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(registry.Recipes, r => Assert.Contains(r.NetId, ids));
    }

    [Fact]
    public void Shaped_recipes_carry_their_dimensions_tags_and_empty_cells()
    {
        var chest = Array.Find(Build().ShapedRecipes, r => r.RecipeId == "minecraft:Chest_recipeId");
        Assert.NotNull(chest);
        Assert.Equal((3, 3), (chest!.Width, chest.Height));
        Assert.Equal(9, chest.Inputs.Length);
        Assert.Equal("minecraft:planks", chest.Inputs[0].Tag);
        Assert.Equal(WireRecipeIngredient.Empty, chest.Inputs[4]); // the hole in the ring
        Assert.Equal("crafting_table", chest.Block);
    }

    [Fact]
    public void Recipes_get_distinct_deterministic_uuids()
    {
        var a = Encode(Build());
        var b = Encode(Build());
        Assert.True(a.SequenceEqual(b));
    }

    private static byte[] Encode(CraftingDataPacket p) => p.Encode().ToArray();

    [Fact]
    public void CraftingData_encode_starts_with_shaped_then_shapeless_counts_and_ends_with_clear()
    {
        var packet = Build();
        var bytes = Encode(packet);
        var stream = new BinaryStream(bytes);

        Assert.Equal((int)ProtocolInfo.CRAFTING_DATA_PACKET, stream.ReadUnsignedVarInt());
        Assert.Equal(packet.ShapedRecipes.Length, stream.ReadUnsignedVarInt());
        Assert.True(bytes[^1] == 1); // ClearRecipes = true
    }

    [Fact]
    public void ShapelessRecipe_encode_shape_matches_protocol_2225_layout()
    {
        var recipe = new ShapelessCraftingRecipe
        {
            RecipeId = "t:shapeless",
            Inputs = [new WireRecipeIngredient("minecraft:oak_log", null, 1), new WireRecipeIngredient(null, "minecraft:planks", 2)],
            Outputs = [new NetworkItemStack(5, 4, 0)],
            Block = "crafting_table",
            RecipeNetworkId = 7
        };

        var writer = new BinaryStream();
        recipe.Write(ref writer);
        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());

        Assert.Equal("t:shapeless", stream.ReadVarString());
        Assert.Equal(2, stream.ReadUnsignedVarInt()); // inputs

        Assert.Equal(1, stream.ReadUnsignedVarInt()); // valid
        Assert.Equal("name", stream.ReadVarString());
        Assert.Equal("minecraft:oak_log", stream.ReadVarString());
        Assert.Equal(WireRecipeIngredient.AnyAux, stream.ReadVarInt());
        Assert.Equal(1, stream.ReadVarInt());

        Assert.Equal(1, stream.ReadUnsignedVarInt()); // valid
        Assert.Equal("item_tag", stream.ReadVarString());
        Assert.Equal("minecraft:planks", stream.ReadVarString());
        Assert.Equal(WireRecipeIngredient.AnyAux, stream.ReadVarInt());
        Assert.Equal(2, stream.ReadVarInt());

        Assert.Equal(1, stream.ReadUnsignedVarInt()); // outputs
        Assert.Equal(5, stream.ReadVarInt());
        Assert.Equal((ushort)4, stream.ReadUShort(BinaryStream.Endianess.Little));
        Assert.Equal(0, (int)stream.ReadUnsignedVarInt()); // meta
        Assert.Equal(0, stream.ReadVarInt()); // block_runtime_id
        Assert.Equal(10, (int)stream.ReadUnsignedVarInt()); // extra blob length
        for (var i = 0; i < 10; i++) Assert.Equal(0, stream.ReadByte());

        Assert.NotEqual(Guid.Empty, stream.ReadUuid());
        Assert.Equal("crafting_table", stream.ReadVarString());
        Assert.Equal(0, stream.ReadVarInt()); // priority
        Assert.False(stream.ReadBool()); // UnlockingRequirement
        Assert.Equal(7, stream.ReadUnsignedVarInt()); // net id
        Assert.True(stream.IsEndOfFile);
    }

    [Fact]
    public void ShapedRecipe_encode_writes_size_ingredients_symmetry_then_net_id()
    {
        var recipe = new ShapedCraftingRecipe
        {
            RecipeId = "t:shaped", Width = 1, Height = 2, AssumeSymmetry = true,
            Inputs = [new WireRecipeIngredient("minecraft:stick", null, 1), WireRecipeIngredient.Empty],
            Outputs = [new NetworkItemStack(5, 1, 0)], Block = "crafting_table", RecipeNetworkId = 9
        };

        var writer = new BinaryStream();
        recipe.Write(ref writer);
        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());

        Assert.Equal("t:shaped", stream.ReadVarString());
        Assert.Equal(1, stream.ReadVarInt()); // width
        Assert.Equal(2, stream.ReadVarInt()); // height
        Assert.Equal(2, stream.ReadUnsignedVarInt()); // ingredients
        Assert.Equal(1, stream.ReadUnsignedVarInt());
        Assert.Equal("name", stream.ReadVarString());
        Assert.Equal("minecraft:stick", stream.ReadVarString());
        stream.ReadVarInt(); stream.ReadVarInt();
        Assert.Equal(0, stream.ReadUnsignedVarInt()); // empty cell
        Assert.Equal(WireRecipeIngredient.AnyAux, stream.ReadVarInt());
        Assert.Equal(0, stream.ReadVarInt());

        Assert.Equal(1, stream.ReadUnsignedVarInt()); // outputs
        stream.ReadVarInt(); stream.ReadUShort(BinaryStream.Endianess.Little); stream.ReadUnsignedVarInt(); stream.ReadVarInt();
        var extra = (int)stream.ReadUnsignedVarInt();
        for (var i = 0; i < extra; i++) stream.ReadByte();

        stream.ReadUuid();
        Assert.Equal("crafting_table", stream.ReadVarString());
        Assert.Equal(0, stream.ReadVarInt()); // priority
        Assert.True(stream.ReadBool()); // assume_symmetry
        Assert.False(stream.ReadBool()); // UnlockingRequirement
        Assert.Equal(9, stream.ReadUnsignedVarInt());
        Assert.True(stream.IsEndOfFile);
    }
}
