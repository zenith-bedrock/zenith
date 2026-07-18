using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>One shapeless recipe entry for CraftingData (type 0 + unlock context).</summary>
sealed class ShapelessCraftingRecipe
{
    public const int TypeShapeless = 0;
    public const byte UnlockAlways = 1; // RecipeUnlockContextAlwaysUnlocked

    public string RecipeId { get; set; } = "";
    public DefaultDescriptorInput[] Inputs { get; set; } = [];
    public NetworkItemStack[] Outputs { get; set; } = [];
    public string Block { get; set; } = "crafting_table";
    public int Priority { get; set; }
    public uint RecipeNetworkId { get; set; }

    public void Write(ref BinaryStream writer)
    {
        writer.WriteVarInt(TypeShapeless);
        writer.WriteVarString(RecipeId);

        writer.WriteUnsignedVarInt(Inputs.Length);
        foreach (var input in Inputs)
        {
            writer.WriteByte(ItemDescriptorType.Default);
            writer.WriteShort(input.NetworkId, BinaryStream.Endianess.Little);
            if (input.NetworkId != 0)
                writer.WriteShort(input.Metadata, BinaryStream.Endianess.Little);
            writer.WriteVarInt(input.Count);
        }

        writer.WriteUnsignedVarInt(Outputs.Length);
        foreach (var output in Outputs)
            output.WriteItemStack(ref writer);

        writer.WriteUuid(Guid.Empty);
        writer.WriteVarString(Block);
        writer.WriteVarInt(Priority);
        writer.WriteByte(UnlockAlways);
        writer.WriteUnsignedVarInt((int)RecipeNetworkId);
    }
}

readonly record struct DefaultDescriptorInput(short NetworkId, short Metadata, int Count);

/// <summary>
/// CraftingData (0x34) — remints RecipeRegistry net ids to the client (ADR §35).
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
        writer.WriteUnsignedVarInt(Recipes.Length);
        foreach (var recipe in Recipes)
            recipe.Write(ref writer);

        writer.WriteUnsignedVarInt(0); // PotionRecipes
        writer.WriteUnsignedVarInt(0); // PotionContainerChangeRecipes
        writer.WriteUnsignedVarInt(0); // MaterialReducers
        writer.WriteBool(ClearRecipes);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
