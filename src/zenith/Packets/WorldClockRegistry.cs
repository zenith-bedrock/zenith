using Zenith.Binary;
using Zenith.Reference;

namespace Zenith.Packets;

/// <summary>
/// The overworld clock as BDS announces it: its id, name and the six time markers (sunrise, day, noon, sunset, night,
/// midnight) come from the embedded capture, decoded once; only the time is ours. Builds the registry and state packets
/// for a given world time.
/// </summary>
static class WorldClockRegistry
{
    private static readonly Lazy<WorldClock> Overworld = new(Load);

    public static SyncWorldClocksPacket StatePacket(int worldTime) => new()
    {
        PayloadType = SyncWorldClocksPacket.PayloadSyncState,
        States = [new WorldClockState(Overworld.Value.Id, worldTime, Overworld.Value.Paused)],
    };

    public static SyncWorldClocksPacket RegistryPacket(int worldTime) => new()
    {
        PayloadType = SyncWorldClocksPacket.PayloadInitializeRegistry,
        Clocks = [Overworld.Value with { Time = worldTime }],
    };

    private static WorldClock Load()
    {
        foreach (var entry in JoinStaticData.Named(JoinStaticNames.WorldClocks))
        {
            var stream = new BinaryStream(entry.Body);
            try
            {
                var packet = new SyncWorldClocksPacket();
                packet.Decode(ref stream);
                if (packet.PayloadType == SyncWorldClocksPacket.PayloadInitializeRegistry)
                    return packet.Clocks.Single(c => c.Name == "minecraft:overworld");
            }
            finally
            {
                stream.Dispose();
            }
        }

        throw new InvalidOperationException("No captured world clock registry is embedded.");
    }
}
