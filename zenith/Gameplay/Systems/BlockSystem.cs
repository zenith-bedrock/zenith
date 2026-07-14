using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Aplica <see cref="BlockEditIntent"/> no tick e replica UpdateBlock aos peers in-game.
/// Mutação de mundo: overlay esparso permanente (nunca reescreve subchunk).
/// Place consome 1 do hotbar; break dá o bloco ao inventário (sem item actor no chão).
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
            while (player.TryConsumeBlockEdit(out var edit))
                ApplyEdit(player, edit);
        }
    }

    private void ApplyEdit(global::Zenith.Player.Player player, in BlockEditIntent edit)
    {
        var inventoryChanged = false;
        if (edit.BlockRuntimeId != World.World.AirRuntimeId)
        {
            var slot = edit.HotbarSlot;
            if (!PlayerInventory.IsValidHotbarSlot(slot) || !player.Inventory.TryConsumeOne(slot))
                return;
            inventoryChanged = true;
        }
        else
        {
            var previous = _world.GetBlock(edit.X, edit.Y, edit.Z);
            if (previous != World.World.AirRuntimeId && player.Inventory.TryAdd(previous))
                inventoryChanged = true;
        }

        _world.SetBlock(edit.X, edit.Y, edit.Z, edit.BlockRuntimeId);

        if (inventoryChanged)
            player.Session.Protocol.Inventory.SendHotbarContent(player.Inventory);

        foreach (var peer in _players.Online)
        {
            if (!peer.IsInGame) continue;
            peer.Session.Protocol.World.SendUpdateBlock(edit.X, edit.Y, edit.Z, edit.BlockRuntimeId);
        }
    }
}
