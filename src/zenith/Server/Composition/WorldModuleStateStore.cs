using Zenith.Gameplay.Modules;

namespace Zenith.Server.Composition;

/// <summary>Module state kept with the world: the world's own storage and write-behind, so regions travel and back up with the world folder.</summary>
sealed class WorldModuleStateStore(World.World world) : IModuleStateStore
{
    public byte[]? Load(string module) => world.LoadModuleState(module);

    public void Save(string module, byte[] state) => world.PersistModuleState(module, state);
}
