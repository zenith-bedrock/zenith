namespace Zenith.Gameplay;

/// <summary>
/// Vanilla-parity XP level thresholds and level-up math (Phase XI.4). Pure functions over
/// (level, points) — no state of its own, no generic progression/attribute framework.
/// </summary>
static class PlayerExperience
{
    /// <summary>Points required to advance from <paramref name="level"/> to the next one.</summary>
    public static int PointsToNextLevel(int level) => level switch
    {
        < 0 => PointsToNextLevel(0),
        < 16 => 2 * level + 7,
        < 31 => 5 * level - 38,
        _ => 9 * level - 158
    };

    /// <summary>
    /// Adds <paramref name="amount"/> points to the current (level, points) pair, applying every
    /// level-up the addition crosses in one pass.
    /// </summary>
    public static (int Level, int Points) AddPoints(int level, int points, int amount)
    {
        if (amount <= 0) return (level, points);

        points += amount;
        while (points >= PointsToNextLevel(level))
        {
            points -= PointsToNextLevel(level);
            level++;
        }

        return (level, points);
    }

    /// <summary>Wire fraction (0..1) toward the next level — <c>minecraft:player.experience</c>.</summary>
    public static float Progress(int level, int points)
    {
        var required = PointsToNextLevel(level);
        return required <= 0 ? 0f : Math.Clamp(points / (float)required, 0f, 1f);
    }
}
