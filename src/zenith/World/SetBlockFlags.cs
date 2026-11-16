namespace Zenith.World;

/// <summary>What a world write sets in motion besides changing the cell.</summary>
[Flags]
enum SetBlockFlags : byte
{
    /// <summary>Only the cell changes: bulk or cosmetic writes that nothing around them depends on.</summary>
    None = 0,

    /// <summary>The cell is queued so its neighbours re-check support and shape (<see cref="BlockUpdateQueue"/>).</summary>
    NotifyNeighbors = 1,

    /// <summary>A normal gameplay write.</summary>
    Default = NotifyNeighbors
}
