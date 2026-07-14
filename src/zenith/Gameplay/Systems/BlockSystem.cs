using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Aplica <see cref="BlockEditIntent"/> no tick e replica UpdateBlock aos peers in-game.
/// Mutação de mundo: overlay esparso permanente (nunca reescreve subchunk).
/// Place consome 1 do hotbar; break dá o bloco ao inventário (sem item actor no chão).
/// Autoridade: reach / célula / inventario antes de SetBlock.
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
        if (!edit.IsInWorldBounds())
            return;

        if (!IsWithinReach(player, edit.X, edit.Y, edit.Z))
            return;

        var inventoryChanged = false;
        if (edit.BlockRuntimeId != World.World.AirRuntimeId)
        {
            // Place: target must be air, then consume, then mutate.
            if (_world.GetBlock(edit.X, edit.Y, edit.Z) != World.World.AirRuntimeId)
                return;

            var slot = edit.HotbarSlot;
            if (!PlayerInventory.IsValidHotbarSlot(slot) || !player.Inventory.TryConsumeOne(slot))
                return;
            inventoryChanged = true;
        }
        else
        {
            // Break: must hit a real block; TryAdd must succeed or world stays intact.
            var previous = _world.GetBlock(edit.X, edit.Y, edit.Z);
            if (previous == World.World.AirRuntimeId)
                return;
            if (!player.Inventory.TryAdd(previous))
                return;
            inventoryChanged = true;
        }

        _world.SetBlock(edit.X, edit.Y, edit.Z, edit.BlockRuntimeId);

        if (inventoryChanged)
            player.Session.Protocol.Inventory.SendInventoryContent(player.Inventory);

        foreach (var peer in _players.Online)
        {
            if (!peer.IsInGame) continue;
            peer.Session.Protocol.World.SendUpdateBlock(edit.X, edit.Y, edit.Z, edit.BlockRuntimeId);
        }
    }

    /// <summary>Euclidean reach from eye position to block center.</summary>
    internal static bool IsWithinReach(global::Zenith.Player.Player player, int x, int y, int z)
    {
        var eyeX = player.PositionX;
        var eyeY = player.PositionY + Blocks.PlayerEyeHeight;
        var eyeZ = player.PositionZ;
        var dx = eyeX - (x + 0.5f);
        var dy = eyeY - (y + 0.5f);
        var dz = eyeZ - (z + 0.5f);
        var max = Player.Player.MaxBlockReach;
        return dx * dx + dy * dy + dz * dz <= max * max;
    }
}
