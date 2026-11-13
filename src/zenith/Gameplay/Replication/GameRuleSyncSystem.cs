using Zenith.Gameplay.Runtime;
using Zenith.Rulebook;

namespace Zenith.Gameplay.Replication;

/// <summary>
/// Replicates and persists game rule changes. Whatever changed a rule (the <c>/gamerule</c> command, later a config
/// reload) only touches <see cref="GameRules"/>; this system notices the new <see cref="GameRules.Version"/> and sends
/// the complete list to every online player (what BDS does with <c>game_rules_changed</c>) and writes it to storage.
/// A player who joins later gets the current list from the join sequence, so nothing is replayed to them here.
/// </summary>
sealed class GameRuleSyncSystem(World.World world) : IGameSystem
{
    private long _published = world.Rules.Version;

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        var rules = world.Rules;
        var version = rules.Version;
        if (version == _published) return;
        _published = version;

        var snapshot = rules.Snapshot;
        foreach (var player in online)
            player.Client.World.SendGameRules(snapshot);
        world.PersistGameRules();
    }
}
