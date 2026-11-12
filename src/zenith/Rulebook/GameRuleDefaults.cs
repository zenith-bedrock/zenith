using Zenith.Reference;

namespace Zenith.Rulebook;

/// <summary>
/// The game rules of a fresh world, read from the stock BDS capture embedded as
/// <see cref="JoinStaticNames.GameRules"/> (<c>bedrock-data join-static</c>) rather than typed in: the names, their order,
/// their types and their defaults are exactly what BDS 1.26.63.1 sent.
/// </summary>
static class GameRuleDefaults
{
    public static List<GameRule> Load()
    {
        var entry = JoinStaticData.All.FirstOrDefault(e => e.Name == JoinStaticNames.GameRules);
        if (entry.Body is null) throw new InvalidOperationException("No captured game rules are embedded.");
        return GameRuleCodec.FromBytes(entry.Body);
    }

    public static GameRules Create() => new(Load());
}
