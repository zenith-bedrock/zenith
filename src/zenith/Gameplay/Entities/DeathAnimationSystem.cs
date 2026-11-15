using Zenith.Ecs;
using Zenith.Gameplay.Runtime;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// Ends the death animation of every killed actor: a kill announces the death event and leaves a corpse entity
/// (<see cref="DeathAnimation"/>); once its animation time is over this removes the actor from the peers that saw it die and
/// destroys the corpse. Without this the removal followed the death event in the same tick and clients never played the animation.
/// It owns the presentation lifetime only: loot, experience and the actor's destruction already happened at the kill.
/// </summary>
sealed class DeathAnimationSystem(EntityRuntime stores) : IGameSystem
{
    private readonly List<EntityId> _expired = [];

    /// <summary>Corpses still being shown; diagnostics and tests.</summary>
    internal int Pending => stores.Corpses.Count;

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        _expired.Clear();
        foreach (var id in stores.Corpses.Entities)
            if (stores.Corpses.TryGet(id, out var corpse) && corpse.RemoveAtTick <= clock.CurrentTick)
                _expired.Add(id);

        foreach (var id in _expired)
        {
            var corpse = stores.Corpses.GetRef(id);
            foreach (var peer in online)
                if (Array.IndexOf(corpse.ViewerRuntimeIds, peer.RuntimeId) >= 0)
                    peer.Client.Entity.SendRemoveActor(corpse.ActorUniqueId);
            stores.Entities.Destroy(id);
        }
    }
}
