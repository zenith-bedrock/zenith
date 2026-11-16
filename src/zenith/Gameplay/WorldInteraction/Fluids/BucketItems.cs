using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction.Fluids;

/// <summary>The bucket items the server knows how to use, resolved once against the item palette.</summary>
sealed class BucketItems
{
    public const string EmptyName = "minecraft:bucket";
    public const string WaterName = "minecraft:water_bucket";

    public BucketItems(ItemPalette palette)
    {
        Empty = StackId.FromItem(palette.Require(EmptyName));
        Water = StackId.FromItem(palette.Require(WaterName));
    }

    public StackId Empty { get; }

    public StackId Water { get; }

    public bool IsBucket(StackId id) => id == Empty || id == Water;
}
