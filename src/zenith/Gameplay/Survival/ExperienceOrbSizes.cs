namespace Zenith.Gameplay.Survival;

/// <summary>How an amount of experience is cut into orbs: the vanilla sizes, largest first (the client picks the sprite by value).</summary>
static class ExperienceOrbSizes
{
    private static readonly int[] Sizes = [2477, 1237, 617, 307, 149, 73, 37, 17, 7, 3, 1];

    /// <summary>The largest orb size that fits in <paramref name="amount"/> (1 for anything smaller).</summary>
    public static int LargestFitting(int amount)
    {
        foreach (var size in Sizes)
            if (amount >= size) return size;
        return 1;
    }

    /// <summary>The orbs for <paramref name="amount"/> points, largest first; they add up to exactly the amount.</summary>
    public static List<int> Split(int amount)
    {
        var orbs = new List<int>();
        while (amount > 0)
        {
            var size = LargestFitting(amount);
            orbs.Add(size);
            amount -= size;
        }

        return orbs;
    }
}
