using Zenith.Player;

namespace Zenith.Network;

/// <summary>
/// Orquestra a ordem do fan-out join/leave. Não é GameSystem / VisibilitySystem —
/// só sequencia Protocol a partir do estado atual do <see cref="Player"/>.
/// </summary>
static class PlayerVisibility
{
    public static void AnnounceJoin(Player.Player joiner, IReadOnlyCollection<Player.Player> online)
    {
        var peers = online
            .Where(p => p.IsInGame && !ReferenceEquals(p, joiner))
            .ToArray();

        // 1. Joiner vê peers já in-game
        foreach (var peer in peers)
        {
            SendPlayerListAdd(joiner, peer);
            SendAddPlayer(joiner, peer);
        }

        // 2. Todos (incl. joiner) recebem PlayerList(ADD joiner)
        SendPlayerListAdd(joiner, joiner);
        foreach (var peer in peers)
            SendPlayerListAdd(peer, joiner);

        // 3. Cada peer recebe AddPlayer(joiner) — não enviar AddPlayer de self pra self
        foreach (var peer in peers)
            SendAddPlayer(peer, joiner);
    }

    public static void AnnounceLeave(Player.Player leaving, IReadOnlyCollection<Player.Player> online)
    {
        foreach (var peer in online)
        {
            if (!peer.IsInGame || ReferenceEquals(peer, leaving)) continue;
            peer.Session.Protocol.Entity.SendPlayerListRemove(leaving.Uuid);
            peer.Session.Protocol.Entity.SendRemoveActor(leaving.RuntimeId);
        }
    }

    private static void SendPlayerListAdd(Player.Player recipient, Player.Player subject)
    {
        recipient.Session.Protocol.Entity.SendPlayerListAdd(
            subject.Uuid,
            subject.RuntimeId,
            subject.Username,
            subject.SkinRgba,
            subject.SkinWidth,
            subject.SkinHeight);
    }

    private static void SendAddPlayer(Player.Player recipient, Player.Player subject)
    {
        var held = subject.Session.Protocol.Inventory.DescribeSlot(
            subject.Inventory,
            subject.SelectedHotbarSlot);
        recipient.Session.Protocol.Entity.SendAddPlayer(
            subject.Uuid,
            subject.Username,
            (ulong)subject.RuntimeId,
            subject.PositionX,
            subject.PositionY,
            subject.PositionZ,
            subject.Pitch,
            subject.Yaw,
            subject.HeadYaw,
            held);
    }
}
