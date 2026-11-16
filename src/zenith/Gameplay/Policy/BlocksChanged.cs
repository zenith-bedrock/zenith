using System.Collections.Immutable;

namespace Zenith.Gameplay.Policy;

/// <summary>
/// Fact: a player's block edit was applied in full. Published once per edit by the owner after every effect is done, with every
/// cell the edit changed (a coupled block's other half included). Values only; a refused edit never produces one.
/// </summary>
sealed record BlocksChanged(
    long OperationId,
    Guid ActorId,
    long ActorSessionId,
    BlockEditKind Kind,
    ulong Tick,
    ImmutableArray<BlockCellChange> Cells);
