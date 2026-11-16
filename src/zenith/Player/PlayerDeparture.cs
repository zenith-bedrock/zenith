using Zenith.Event;

namespace Zenith.Player;

/// <summary>A departure handed from the network thread to the game loop: who left the world and why. Values only.</summary>
readonly record struct PlayerDeparture(Guid PlayerId, string Username, long SessionId, PlayerLeaveReason Reason);
