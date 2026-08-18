namespace Zenith.Ecs;

/// <summary>
/// Creeper's feature-specific state: retained target plus fuse timing. Same shape as
/// <see cref="SpiderState"/> by construction — the extra <see cref="IsFusing"/>/
/// <see cref="FuseStartedTick"/> fields are CreeperSystem-owned and have no meaning outside it.
/// </summary>
struct CreeperState
{
    public long? TargetPlayerRuntimeId;
    public bool IsFusing;
    public ulong FuseStartedTick;
}
