namespace Zenith.Event;

/// <summary>Why a player's time in the world ended.</summary>
enum PlayerLeaveReason { Disconnected, TimedOut, Removed }

/// <summary>
/// Fact: a player finished spawning and is now part of the world. Published once per session, by the gameplay owner, after the
/// transition. Carries values only (no live player), so an observer can neither mutate nor hold on to domain state.
/// </summary>
/// <param name="PlayerId">The player's canonical identity.</param>
/// <param name="SessionId">This login's id (the player's runtime id); distinguishes it from a later login of the same identity.</param>
sealed record PlayerEnteredWorld(Guid PlayerId, string Username, long SessionId, ulong Tick);

/// <summary>
/// Fact: a player who had entered the world is gone and the owner has finished the departure work queued for them (inventory
/// persistence and container release run before it). Published once per entered session, never for a login that dropped before
/// spawning.
/// </summary>
sealed record PlayerLeftWorld(Guid PlayerId, string Username, long SessionId, PlayerLeaveReason Reason, ulong Tick);
