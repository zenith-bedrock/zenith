namespace Zenith.Gameplay.Modules;

/// <summary>
/// Where an internal module keeps its own state between runs: opaque bytes, one record per module name, stored with the world it
/// belongs to. A module never learns where or how: it gets bytes back at boot and hands bytes over after a change. Saving is
/// write-behind (the bytes are queued and reach disk with the world's other writes), so it never blocks the caller.
/// </summary>
interface IModuleStateStore
{
    /// <summary>The bytes last saved under <paramref name="module"/>, or null when it never saved. Boot only.</summary>
    byte[]? Load(string module);

    void Save(string module, byte[] state);
}
