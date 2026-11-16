using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction.Fluids;

/// <summary>A bucket holding a block: what the item is, the block it pours out, and the liquid that block is (if any).</summary>
readonly record struct FilledBucket(StackId Item, int Block, FluidType Fluid);

/// <summary>The bucket items the server knows how to use, resolved once against the item palette and block catalog.</summary>
sealed class BucketItems
{
    public const string EmptyName = "minecraft:bucket";
    public const string WaterName = "minecraft:water_bucket";
    public const string MilkName = "minecraft:milk_bucket";
    public const string LavaName = "minecraft:lava_bucket";
    public const string PowderSnowName = "minecraft:powder_snow_bucket";
    private const string PowderSnowBlock = "minecraft:powder_snow";

    private readonly FilledBucket[] _filled;

    public BucketItems(ItemPalette palette)
    {
        Empty = StackId.FromItem(palette.Require(EmptyName));
        if (!Blocks.TryGetDefaultRuntimeId(PowderSnowBlock, out var powderSnow))
            throw new InvalidOperationException($"{PowderSnowBlock} is not in the block palette.");

        Water = new FilledBucket(StackId.FromItem(palette.Require(WaterName)), Blocks.Water, FluidType.Water);
        _filled =
        [
            Water,
            new FilledBucket(StackId.FromItem(palette.Require(LavaName)), Blocks.Lava, FluidType.Lava),
            new FilledBucket(StackId.FromItem(palette.Require(PowderSnowName)), powderSnow, FluidType.None)
        ];
    }

    public StackId Empty { get; }

    /// <summary>The water bucket, which other systems (fish, scooping) treat specially.</summary>
    public FilledBucket Water { get; }

    public bool IsBucket(StackId id) => id == Empty || TryGetFilled(id, out _);

    /// <summary>The filled bucket an item is.</summary>
    public bool TryGetFilled(StackId item, out FilledBucket bucket)
    {
        foreach (var filled in _filled)
            if (filled.Item == item)
            {
                bucket = filled;
                return true;
            }

        bucket = default;
        return false;
    }

    /// <summary>The filled bucket an empty one becomes on scooping this block (water and lava only at depth 0: sources).</summary>
    public bool TryGetScooped(int block, out FilledBucket bucket)
    {
        foreach (var filled in _filled)
            if (filled.Block == block)
            {
                bucket = filled;
                return true;
            }

        bucket = default;
        return false;
    }
}
