using System.Collections.Generic;
using Zenith.Gameplay.Runtime;
using Zenith.Player;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Aplica <see cref="InventoryStackIntent"/> no tick e responde ItemStackResponse (same-session).
/// </summary>
sealed class InventorySystem : IGameSystem
{
    private readonly PlayerManager _players;

    public InventorySystem(PlayerManager players) => _players = players;

    public void Tick(GameClock clock)
    {
        _ = clock;
        if (_players.Count == 0) return;

        foreach (var player in _players.Online)
        {
            while (player.TryConsumeInventoryStack(out var intent))
                Apply(player, intent);
        }
    }

    private static void Apply(global::Zenith.Player.Player player, in InventoryStackIntent intent)
    {
        var inventory = player.Inventory;
        var snapshot = inventory.CaptureSnapshot();
        var touched = new List<int>();
        var ok = true;

        foreach (var action in intent.Actions)
        {
            if (action.Kind == InventoryStackActionKind.Swap)
            {
                if (!inventory.TrySwap(action.From, action.To))
                {
                    ok = false;
                    break;
                }

                AddTouched(touched, action.From);
                AddTouched(touched, action.To);
            }
            else
            {
                var count = action.Count;
                if (count == 0)
                    count = inventory.Get(action.From).Count;
                if (!inventory.TryTransfer(action.From, action.To, count))
                {
                    ok = false;
                    break;
                }

                AddTouched(touched, action.From);
                AddTouched(touched, action.To);
            }
        }

        var protocol = player.Session.Protocol.Inventory;
        if (!ok)
        {
            inventory.RestoreSnapshot(snapshot);
            protocol.SendItemStackResponseError(intent.RequestId);
            protocol.SendInventoryContent(inventory);
            return;
        }

        protocol.SendItemStackResponseOk(intent.RequestId, inventory, touched);
    }

    private static void AddTouched(List<int> touched, int flat)
    {
        if (!touched.Contains(flat))
            touched.Add(flat);
    }
}
