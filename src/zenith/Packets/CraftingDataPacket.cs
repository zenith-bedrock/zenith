using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// One shapeless recipe entry for CraftingData, protocol 2168+ shape (ADR §86). No per-entry
/// type tag — CraftingDataPacket now carries recipes in separate per-type arrays, and the
/// recipe's own <c>Type</c> is which array it's in, not a field on the entry.
/// </summary>
sealed class ShapelessCraftingRecipe
{
    /// <summary>ItemDescriptor variant for a plain named item (not MoLang/item-tag/deferred).</summary>
    private const int DescriptorDefault = 1;

    /// <summary>Descriptor-kind label written alongside the variant number (Cereal map-style encoding).</summary>
    private const string DescriptorKindDefault = "name";

    public string RecipeId { get; set; } = "";
    public DefaultDescriptorInput[] Inputs { get; set; } = [];
    public NetworkItemStack[] Outputs { get; set; } = [];
    public string Block { get; set; } = "crafting_table";
    public int Priority { get; set; }
    public uint RecipeNetworkId { get; set; }

    public void Write(ref BinaryStream writer)
    {
        writer.WriteVarString(RecipeId);

        writer.WriteUnsignedVarInt(Inputs.Length);
        foreach (var input in Inputs)
        {
            writer.WriteUnsignedVarInt(DescriptorDefault);
            writer.WriteVarString(DescriptorKindDefault);
            writer.WriteVarString(input.Name);
            writer.WriteVarInt(input.Metadata);
            writer.WriteVarInt(input.Count);
        }

        writer.WriteUnsignedVarInt(Outputs.Length);
        foreach (var output in Outputs)
            output.WriteItemStack(ref writer);

        writer.WriteUuid(Guid.Empty);
        writer.WriteVarString(Block);
        writer.WriteVarInt(Priority);
        writer.WriteBool(false); // UnlockingRequirement Optional — none (always craftable)
        writer.WriteUnsignedVarInt((int)RecipeNetworkId);
    }
}

readonly record struct DefaultDescriptorInput(string Name, int Metadata, int Count);

/// <summary>
/// CraftingData (0x34) — remints RecipeRegistry net ids to the client (ADR §35), protocol 2168+
/// shape (ADR §86): recipes carried in separate per-recipe-type arrays instead of one combined
/// array with a per-entry type tag. Zenith only ever sends shapeless recipes; every other array
/// is empty by design, not a placeholder for unimplemented recipe kinds.
/// ClearRecipes=true replaces vanilla book with Zenith list only.
/// </summary>
sealed class CraftingDataPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.CRAFTING_DATA_PACKET;

    public ShapelessCraftingRecipe[] Recipes { get; set; } = [];
    public bool ClearRecipes { get; set; } = true;

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);

        writer.WriteUnsignedVarInt(0); // ShapedRecipes
        writer.WriteUnsignedVarInt(Recipes.Length); // ShapelessRecipes
        foreach (var recipe in Recipes)
            recipe.Write(ref writer);
        writer.WriteUnsignedVarInt(0); // MultiRecipes
        writer.WriteUnsignedVarInt(0); // UserDataShapelessRecipes
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
