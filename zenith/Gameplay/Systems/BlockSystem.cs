using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Aplica <see cref="BlockEditIntent"/> no tick e replica UpdateBlock aos peers in-game.
/// Mutação de mundo: overlay esparso em <see cref="World.World"/> (não CoW de coluna).
/// </summary>
sealed class BlockSystem : IGameSystem
{
    private readonly PlayerManager _players;
    private readonly World.World _world;

    public BlockSystem(PlayerManager players, World.World world)
    {
        _players = players;
        _world = world;
    }

    public void Tick(GameClock clock)
    {
        _ = clock;
        if (_players.Count == 0) return;

        foreach (var player in _players.Online)
        {
            if (!player.TryConsumeBlockEdit(out var edit)) continue;

            _world.SetBlock(edit.X, edit.Y, edit.Z, edit.BlockRuntimeId);

            foreach (var peer in _players.Online)
            {
                if (!peer.IsInGame) continue;
                peer.Session.Protocol.World.SendUpdateBlock(edit.X, edit.Y, edit.Z, edit.BlockRuntimeId);
            }
        }
    }
}
