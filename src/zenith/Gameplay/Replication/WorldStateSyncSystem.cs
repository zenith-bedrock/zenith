using Zenith.Gameplay.Runtime;

namespace Zenith.Gameplay.Replication;

/// <summary>
/// Replicates and stores changes to the world's settings (<see cref="WorldStates"/>): game rules, difficulty, weather. Whatever changed one
/// (a command, the weather cycle, later a config reload) only touches the setting; this system notices its new version, sends it to every
/// online player (what BDS does with <c>game_rules_changed</c>, <c>set_difficulty</c> and the weather level events) and writes it to storage.
/// A player who joins later gets the current values from the join sequence, so nothing is replayed to them here.
/// </summary>
[GameSystem("world-state-sync")]
sealed class WorldStateSyncSystem(World.World world) : IGameSystem
{
    private readonly IReadOnlyList<WorldStateBinding> _states = WorldStates.For(world);
    private readonly long[] _published = WorldStates.For(world).Select(state => state.Version()).ToArray();

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        for (var i = 0; i < _states.Count; i++)
        {
            var version = _states[i].Version();
            if (version == _published[i]) continue;
            _published[i] = version;
            foreach (var player in online)
                _states[i].Send(player.Client.World);
            _states[i].Save(world);
        }
    }
}
