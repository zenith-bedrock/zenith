namespace Zenith.Rulebook;

/// <summary>The world difficulty, with the wire values Bedrock uses (plan 004 item 20).</summary>
enum Difficulty
{
    Peaceful = 0,
    Easy = 1,
    Normal = 2,
    Hard = 3
}

static class DifficultyRules
{
    public static bool TryParse(string text, out Difficulty difficulty)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "peaceful" or "p" or "0": difficulty = Difficulty.Peaceful; return true;
            case "easy" or "e" or "1": difficulty = Difficulty.Easy; return true;
            case "normal" or "n" or "2": difficulty = Difficulty.Normal; return true;
            case "hard" or "h" or "3": difficulty = Difficulty.Hard; return true;
            default: difficulty = Difficulty.Normal; return false;
        }
    }

    /// <summary>
    /// What a mob's hit does at this difficulty: Easy takes half (and a point), Normal all of it, Hard half again more;
    /// Peaceful mobs do not hurt at all. Vanilla's <c>getDamageAfterDifficulty</c>.
    /// </summary>
    public static float ScaleMobDamage(Difficulty difficulty, float damage) => difficulty switch
    {
        Difficulty.Peaceful => 0f,
        Difficulty.Easy => MathF.Min(damage / 2f + 1f, damage),
        Difficulty.Hard => damage * 1.5f,
        _ => damage
    };

    /// <summary>The health starvation stops at: 10 on Easy, 1 on Normal, 0 (it kills) on Hard.</summary>
    public static float StarvationFloor(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => 10f,
        Difficulty.Hard => 0f,
        _ => 1f
    };
}

/// <summary>
/// The world's difficulty. Whatever changes it (<c>/difficulty</c>, later a config reload) only touches this; the sync system
/// notices the new <see cref="Version"/>, tells every client and stores it.
/// </summary>
sealed class DifficultySetting
{
    private volatile int _value = (int)Difficulty.Normal;
    private long _version;

    public Difficulty Value => (Difficulty)_value;
    public long Version => Volatile.Read(ref _version);

    public void Set(Difficulty difficulty)
    {
        if (!Enum.IsDefined(difficulty) || (int)difficulty == _value) return;
        _value = (int)difficulty;
        Interlocked.Increment(ref _version);
    }
}
