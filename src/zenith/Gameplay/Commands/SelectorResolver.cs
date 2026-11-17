using Zenith.Ecs;
using Zenith.Player;

namespace Zenith.Gameplay.Commands;

/// <summary>One thing a selector can pick: a player or a mob, with the facts the filters ask about.</summary>
sealed record SelectorCandidate(
    string Type, string Name, float X, float Y, float Z,
    long PlayerSessionId = 0, Guid PlayerId = default, EntityId? Entity = null, int Level = 0, GameMode Mode = GameMode.Survival)
{
    public bool IsPlayer => Type == PlayerType;

    public const string PlayerType = "player";
}

/// <summary>Where the command was run from: the origin of distances, and who <c>@s</c> is.</summary>
readonly record struct SelectorOrigin(float X, float Y, float Z, long CallerSessionId, Guid CallerId);

/// <summary>
/// Applies a <see cref="TargetSelector"/> to the candidates that exist now. Pure: the caller gathers the candidates (on the game loop, where
/// the world cannot change under it) and hands them in, which is also what makes every rule testable without a server.
/// </summary>
static class SelectorResolver
{
    public static IReadOnlyList<SelectorCandidate> Resolve(
        TargetSelector selector, SelectorOrigin origin, IReadOnlyList<SelectorCandidate> candidates, Random random)
    {
        var pool = selector.Kind switch
        {
            SelectorKind.Self => candidates.Where(c => c.IsPlayer && c.PlayerSessionId == origin.CallerSessionId && c.PlayerId == origin.CallerId),
            SelectorKind.Named => candidates.Where(c => c.IsPlayer && string.Equals(c.Name, selector.PlayerName, StringComparison.OrdinalIgnoreCase)),
            SelectorKind.NearestPlayer or SelectorKind.AllPlayers => candidates.Where(c => c.IsPlayer),
            SelectorKind.Random => selector.Types.Any(t => !t.Negated) ? candidates : candidates.Where(c => c.IsPlayer),
            _ => candidates
        };

        var ox = selector.X ?? origin.X;
        var oy = selector.Y ?? origin.Y;
        var oz = selector.Z ?? origin.Z;
        var matched = pool.Where(c => Matches(selector, c, ox, oy, oz)).ToList();

        // Nearest first for @p and @e (and anything with a count); @r shuffles; the rest keep their order.
        if (selector.Kind == SelectorKind.Random)
            Shuffle(matched, random);
        else if (selector.Kind is SelectorKind.NearestPlayer or SelectorKind.AllEntities || selector.Count is not null)
            matched = [.. matched.OrderBy(c => Distance2(c, ox, oy, oz))];

        var count = selector.Count ?? selector.Kind switch
        {
            SelectorKind.NearestPlayer or SelectorKind.Random => 1,
            _ => int.MaxValue
        };

        if (count < 0)
        {
            matched.Reverse(); // the farthest first
            count = -count;
        }

        return matched.Count <= count ? matched : matched.GetRange(0, count);
    }

    private static bool Matches(TargetSelector selector, SelectorCandidate c, float ox, float oy, float oz)
    {
        foreach (var type in selector.Types)
            if ((c.Type == type.Value) == type.Negated) return false;
        foreach (var name in selector.Names)
            if (string.Equals(c.Name, name.Value, StringComparison.OrdinalIgnoreCase) == name.Negated) return false;

        if (selector.Modes.Count > 0)
        {
            if (!c.IsPlayer) return false; // a game mode is a player's
            foreach (var (mode, negated) in selector.Modes)
                if ((c.Mode == mode) == negated) return false;
        }

        if (selector.MaxLevel is not null || selector.MinLevel is not null)
        {
            if (!c.IsPlayer) return false;
            if (c.Level > (selector.MaxLevel ?? int.MaxValue) || c.Level < (selector.MinLevel ?? 0)) return false;
        }

        if (selector.HasVolume)
        {
            // A box from the origin out by dx, dy, dz (each may be negative); the far edge counts the whole block.
            if (!Within(c.X, ox, selector.Dx ?? 0f) || !Within(c.Y, oy, selector.Dy ?? 0f) || !Within(c.Z, oz, selector.Dz ?? 0f)) return false;
        }

        var d2 = Distance2(c, ox, oy, oz);
        if (selector.MaxDistance is { } max && d2 > max * max) return false;
        if (selector.MinDistance is { } min && d2 < min * min) return false;
        return true;
    }

    private static bool Within(float value, float origin, float delta)
    {
        var low = MathF.Min(origin, origin + delta);
        var high = MathF.Max(origin, origin + delta) + 1f;
        return value >= low && value <= high;
    }

    private static float Distance2(SelectorCandidate c, float x, float y, float z)
    {
        var (dx, dy, dz) = (c.X - x, c.Y - y, c.Z - z);
        return dx * dx + dy * dy + dz * dz;
    }

    private static void Shuffle(List<SelectorCandidate> list, Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
