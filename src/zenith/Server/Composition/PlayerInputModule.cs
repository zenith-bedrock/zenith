using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Gameplay.Entities;

namespace Zenith.Server.Composition;

/// <summary>Systems that apply this tick's player input and publish its consequences to the rest of the tick.</summary>
sealed class PlayerInputModule : IGameplayModule
{
    public string Name => "player-input";

    public void Register(ServerServices s)
    {
        s.Register(new TimeSyncSystem(), "time-sync");
        // Movement before Block/Inventory: IsSneaking must be applied before sneak-place / chest open (§53/§56).
        s.Register(new MovementSystem(s.Players), "movement");
        // Rebuilds from this tick's just-applied pose, so every later system (this tick) queries current, not stale,
        // player positions. See PlayerSpatialIndexSystem.
        s.Register(new PlayerSpatialIndexSystem(s.PlayerSpatial), "player-spatial-index");
        // ItemUseOnActor player targets are resolved after movement established this tick's pose.
        s.Register(new PlayerMeleeSystem(s.Players), "player-melee");
        s.Register(new ChatSystem(), "chat");
        s.Register(new GameModeSystem(), "game-mode");
    }
}
