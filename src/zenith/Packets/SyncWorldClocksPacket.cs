using Zenith.Binary;

namespace Zenith.Packets;

/// <summary>One named point on a world clock's cycle (sunrise, noon, ...). <paramref name="Period"/> is the cycle length in ticks when it repeats.</summary>
readonly record struct WorldClockMarker(long Id, string Name, int Time, int? Period);

/// <summary>A world clock and its markers, as announced in the registry.</summary>
sealed record WorldClock(long Id, string Name, int Time, bool Paused, IReadOnlyList<WorldClockMarker> Markers);

/// <summary>The running state of one clock.</summary>
readonly record struct WorldClockState(long ClockId, int Time, bool Paused);

/// <summary>
/// SyncWorldClocks (0x158) — the 1.26 world clock: BDS announces the clock registry once at join and keeps clients in
/// step with state updates. Only the two payloads BDS sends at join are modelled (<see cref="PayloadSyncState"/> and
/// <see cref="PayloadInitializeRegistry"/>); add/remove marker payloads are refused rather than half-read.
/// Layout verified byte for byte against the BDS capture (see the golden test).
/// </summary>
sealed class SyncWorldClocksPacket : DataPacket
{
    public const int PayloadSyncState = 0;
    public const int PayloadInitializeRegistry = 1;

    public override int Id => (int)ProtocolInfo.SYNC_WORLD_CLOCKS_PACKET;

    public int PayloadType { get; set; } = PayloadSyncState;

    public IReadOnlyList<WorldClockState> States { get; set; } = [];

    public IReadOnlyList<WorldClock> Clocks { get; set; } = [];

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUnsignedVarInt(PayloadType);
        switch (PayloadType)
        {
            case PayloadSyncState:
                writer.WriteUnsignedVarInt(States.Count);
                foreach (var state in States)
                {
                    writer.WriteUnsignedVarLong(state.ClockId);
                    writer.WriteVarInt(state.Time);
                    writer.WriteBool(state.Paused);
                }

                break;
            case PayloadInitializeRegistry:
                writer.WriteUnsignedVarInt(Clocks.Count);
                foreach (var clock in Clocks)
                {
                    writer.WriteUnsignedVarLong(clock.Id);
                    writer.WriteVarString(clock.Name);
                    writer.WriteVarInt(clock.Time);
                    writer.WriteBool(clock.Paused);
                    writer.WriteUnsignedVarInt(clock.Markers.Count);
                    foreach (var marker in clock.Markers)
                    {
                        writer.WriteUnsignedVarLong(marker.Id);
                        writer.WriteVarString(marker.Name);
                        writer.WriteVarInt(marker.Time);
                        writer.WriteBool(marker.Period.HasValue);
                        if (marker.Period is { } period) writer.WriteInt(period, BinaryStream.Endianess.Little);
                    }
                }

                break;
            default:
                throw new NotSupportedException($"World clock payload {PayloadType} is not modelled.");
        }

        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream)
    {
        PayloadType = stream.ReadUnsignedVarInt();
        switch (PayloadType)
        {
            case PayloadSyncState:
            {
                var states = new List<WorldClockState>();
                for (var n = stream.ReadUnsignedVarInt(); n > 0; n--)
                    states.Add(new WorldClockState(stream.ReadUnsignedVarLong(), stream.ReadVarInt(), stream.ReadBool()));
                States = states;
                break;
            }
            case PayloadInitializeRegistry:
            {
                var clocks = new List<WorldClock>();
                for (var n = stream.ReadUnsignedVarInt(); n > 0; n--)
                {
                    var id = stream.ReadUnsignedVarLong();
                    var name = stream.ReadVarString();
                    var time = stream.ReadVarInt();
                    var paused = stream.ReadBool();
                    var markers = new List<WorldClockMarker>();
                    for (var m = stream.ReadUnsignedVarInt(); m > 0; m--)
                    {
                        var markerId = stream.ReadUnsignedVarLong();
                        var markerName = stream.ReadVarString();
                        var markerTime = stream.ReadVarInt();
                        int? period = stream.ReadBool() ? stream.ReadInt(BinaryStream.Endianess.Little) : null;
                        markers.Add(new WorldClockMarker(markerId, markerName, markerTime, period));
                    }

                    clocks.Add(new WorldClock(id, name, time, paused, markers));
                }

                Clocks = clocks;
                break;
            }
            default:
                throw new NotSupportedException($"World clock payload {PayloadType} is not modelled.");
        }
    }
}
