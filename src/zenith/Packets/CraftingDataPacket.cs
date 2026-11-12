using Zenith.Binary;

namespace Zenith.Packets;

/// <summary>
/// One recipe ingredient on the wire (<c>RecipeIngredient</c>): empty, a named item or an item tag. Metadata is
/// the wildcard 32767 unless the ingredient requires one aux (ADR §147), as a stock BDS sends it.
/// </summary>
readonly record struct WireRecipeIngredient(string? Name, string? Tag, int Count, int Aux = WireRecipeIngredient.AnyAux)
{
    public const int AnyAux = 32767;

    public static WireRecipeIngredient Empty => new(null, null, 0);

    public void Write(ref BinaryStream writer)
    {
        if (Name is null && Tag is null)
        {
            writer.WriteUnsignedVarInt(0); // invalid / empty cell
            writer.WriteVarInt(AnyAux);
            writer.WriteVarInt(0);
            return;
        }

        writer.WriteUnsignedVarInt(1); // valid
        if (Name is not null)
        {
            writer.WriteVarString("name");
            writer.WriteVarString(Name);
        }
        else
        {
            writer.WriteVarString("item_tag");
            writer.WriteVarString(Tag!);
        }

        writer.WriteVarInt(Aux);
        writer.WriteVarInt(Count);
    }
}

/// <summary>Fields shared by shaped and shapeless entries (protocol 2168+ per-type arrays, ADR §86).</summary>
abstract class CraftingRecipeEntry
{
    public string RecipeId { get; set; } = "";
    public WireRecipeIngredient[] Inputs { get; set; } = [];
    public NetworkItemStack[] Outputs { get; set; } = [];
    public string Block { get; set; } = "crafting_table";
    public int Priority { get; set; }
    public uint RecipeNetworkId { get; set; }

    protected void WriteInputs(ref BinaryStream writer)
    {
        writer.WriteUnsignedVarInt(Inputs.Length);
        foreach (var input in Inputs)
            input.Write(ref writer);
    }

    protected void WriteTail(ref BinaryStream writer)
    {
        writer.WriteUnsignedVarInt(Outputs.Length);
        foreach (var output in Outputs)
            output.WriteItemStack(ref writer);

        writer.WriteUuid(StableUuid(RecipeId));
        writer.WriteVarString(Block);
        writer.WriteVarInt(Priority);
    }

    /// <summary>The client keys recipes by uuid as well as net id, so each recipe gets a deterministic, distinct one.</summary>
    private static Guid StableUuid(string recipeId) =>
        new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(recipeId)));
}

sealed class ShapelessCraftingRecipe : CraftingRecipeEntry
{
    public void Write(ref BinaryStream writer)
    {
        writer.WriteVarString(RecipeId);
        WriteInputs(ref writer);
        WriteTail(ref writer);
        writer.WriteBool(false); // UnlockingRequirement — none (always craftable)
        writer.WriteUnsignedVarInt((int)RecipeNetworkId);
    }
}

sealed class ShapedCraftingRecipe : CraftingRecipeEntry
{
    public int Width { get; set; }
    public int Height { get; set; }
    public bool AssumeSymmetry { get; set; }

    public void Write(ref BinaryStream writer)
    {
        writer.WriteVarString(RecipeId);
        writer.WriteVarInt(Width);
        writer.WriteVarInt(Height);
        WriteInputs(ref writer); // row-major, count == Width * Height
        WriteTail(ref writer);
        writer.WriteBool(AssumeSymmetry);
        writer.WriteBool(false); // UnlockingRequirement — none (always craftable)
        writer.WriteUnsignedVarInt((int)RecipeNetworkId);
    }
}

/// <summary>
/// CraftingData (0x34), protocol 2168+ shape (ADR §86): recipes in separate per-type arrays. Zenith sends shaped
/// and shapeless recipes; every other array is a genuine empty list. ClearRecipes=true replaces the client's
/// built-in book with exactly this list.
/// </summary>
sealed class CraftingDataPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.CRAFTING_DATA_PACKET;

    public ShapedCraftingRecipe[] ShapedRecipes { get; set; } = [];
    public ShapelessCraftingRecipe[] Recipes { get; set; } = [];
    public bool ClearRecipes { get; set; } = true;

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);

        writer.WriteUnsignedVarInt(ShapedRecipes.Length);
        foreach (var recipe in ShapedRecipes)
            recipe.Write(ref writer);
        writer.WriteUnsignedVarInt(Recipes.Length); // ShapelessRecipes
        foreach (var recipe in Recipes)
            recipe.Write(ref writer);
        writer.WriteUnsignedVarInt(0); // MultiRecipes
        writer.WriteUnsignedVarInt(0); // ShulkerBoxRecipes (BDS lists them here; ours are plain shapeless)
        writer.WriteUnsignedVarInt(0); // ShapelessChemistryRecipes
        writer.WriteUnsignedVarInt(0); // ShapedChemistryRecipes
        writer.WriteUnsignedVarInt(0); // SmithingTransformRecipes
        writer.WriteUnsignedVarInt(0); // SmithingTrimRecipes
        writer.WriteUnsignedVarInt(0); // PotionRecipes (potion mixing)
        writer.WriteUnsignedVarInt(0); // PotionContainerChangeRecipes
        writer.WriteUnsignedVarInt(0); // MaterialReducers
        writer.WriteBool(ClearRecipes);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
