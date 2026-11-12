using Zenith.World;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// The development showcase: every species spawns one actor near the first player who joins, so a fresh world has something to
/// look at. It is scaffolding, not game design: a vanilla server does not do it (the zombie killed an idle test bot three seconds
/// after join). It used to be eleven copies of the same code with no way to turn it off; it is now one policy, switched by the
/// <c>world.showcase-mobs</c> config key (default on, to keep current behaviour).
/// </summary>
sealed class ShowcaseMobs(bool enabled = true)
{
    public bool Enabled { get; } = enabled;

    /// <summary>
    /// Where a showcase actor goes: <paramref name="offsetX"/>/<paramref name="offsetZ"/> blocks from the first living in-game player, on the
    /// terrain surface there. False when the showcase is off or nobody is in the game yet.
    /// </summary>
    public bool TryPlace(
        IReadOnlyList<Player.Player> online, World.World world, float offsetX, float offsetZ,
        out float x, out float y, out float z)
    {
        x = y = z = 0f;
        if (!Enabled) return false;
        var player = online.FirstOrDefault(p => p.IsInGame && !p.IsDead);
        if (player is null) return false;
        x = player.PositionX + offsetX;
        z = player.PositionZ + offsetZ;
        y = world.SampleSpawnFeetY((int)MathF.Floor(x), (int)MathF.Floor(z));
        return true;
    }
}
