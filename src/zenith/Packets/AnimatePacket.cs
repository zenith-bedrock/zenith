using Zenith.Packets.Generation;
using Zenith.Binary;

namespace Zenith.Packets;

/// <summary>
/// Animate (0x2c) — server-owned arm swing / crits to peers (§53). Protocol 1001 wire:
/// action u8 + runtime id + data f32 + optional swingSource string.
/// Inbound client Animate stays quiet (no rebroadcast).
/// </summary>
[GamePacket((int)ProtocolInfo.ANIMATE_PACKET)]
sealed partial class AnimatePacket : DataPacket
{
    public const byte ActionSwingArm = 1;
    public const byte ActionStopSleep = 3;
    public const byte ActionCriticalHit = 4;
    public const byte ActionMagicCriticalHit = 5;

    [Wire]
    public byte Action { get; set; }

    [WireVar]
    public ulong ActorRuntimeId { get; set; }

    /// <summary>Always present on wire (rowing used this historically; SwingArm uses 0).</summary>
    [Wire(BinaryStream.Endianess.Little)]
    public float Data { get; set; }

    /// <summary>Optional; e.g. "attack", "mine". Null → optional bool false.</summary>
    [WireString]
    [WireOptional]
    public string? SwingSource { get; set; }

    /// <summary>Optional (2225): which hand animates (HandSlot). Null → optional bool false.</summary>
    [Wire]
    [WireOptional]
    public byte? Hand { get; set; }
}
