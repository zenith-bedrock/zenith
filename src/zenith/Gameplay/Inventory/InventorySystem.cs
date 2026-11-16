using Zenith.Gameplay.Ports;
using System.Collections.Generic;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction;

namespace Zenith.Gameplay.Inventory;

/// <summary>
/// Aplica <see cref="InventoryStackIntent"/> no tick e responde ItemStackResponse (same-session).
/// Slot references are resolved against the authoritative open-container session and player UI.
/// </summary>
sealed class InventorySystem : IGameSystem
{
    private readonly PlayerManager _players;
    private readonly World.World _world;
    private readonly ContainerSessions _windows;
    private readonly StackRequestApplier _requests;

    public InventorySystem(PlayerManager players, World.World world, RecipeRegistry recipes, CreativeCatalog creative)
    {
        _players = players;
        _world = world;
        _windows = new ContainerSessions(world);
        _requests = new StackRequestApplier(players, world, recipes, creative, new InventorySlots(world));
    }

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _ = clock;

        // Disconnect is produced by the network lifecycle, but ChestStore opener state remains
        // gameplay-owned. Drain even when the departing player has already left Online.
        while (_players.TryConsumeDisconnectedContainerCleanup(out var disconnected))
            ChestLidFanout.ReleaseOpener(online, _world, disconnected);

        // Same reasoning (ADR §104b Adendo): PlayerInventory has no lock, so building its save blob must
        // happen here, on the GameLoop thread that owns its mutation — not directly from the
        // network-close callback, which could otherwise race a concurrent Apply for this same
        // player. Persists whatever was last successfully applied; any intent still queued for a
        // now-departed player is simply never processed, not force-applied post-disconnect.
        while (_players.TryConsumeDisconnectedInventoryPersist(out var departed))
        {
            _world.PersistInventory(departed);
            _world.PersistPlayerData(departed);
        }

        if (online.Count == 0) return;

        foreach (var player in online)
        {
            if (player.IsInGame && !player.IsDead && player.Windows.RecoverUiToInventory(player.Inventory))
            {
                player.Client.Inventory.SendInventoryContent(player.Inventory);
                player.Client.Inventory.SendUiInventoryContent(player);
                _world.PersistInventory(player);
            }
            while (player.Windows.TryConsumeInventoryRequest(out var window, out var stack))
            {
                try
                {
                    // A departed player may still occur in this tick's online snapshot.
                    if (!player.IsInGame) continue;
                    if (window is { } lifecycle)
                    {
                        if (!player.IsDead || lifecycle.Action == InventoryWindowIntent.Kind.Close)
                            _windows.Apply(player, lifecycle, online);
                    }
                    else if (!player.IsDead && stack is { } transaction)
                        _requests.Apply(player, transaction, online);
                }
                finally { player.Windows.CompleteInventoryWindow(); }
            }
        }
    }
}
