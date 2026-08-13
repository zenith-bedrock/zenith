using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>MobEffect (0x1c) — server-authoritative add/update/remove of a timed player effect (Phase XI.3).</summary>
[GamePacket((int)ProtocolInfo.MOB_EFFECT_PACKET)]
sealed partial class MobEffectPacket : DataPacket
{
    public const byte EventAdd = 1;
    public const byte EventUpdate = 2;
    public const byte EventRemove = 3;

    [WireVar]
    public long TargetRuntimeId { get; set; }

    [Wire]
    public byte EventId { get; set; }

    [WireVar]
    public int EffectId { get; set; }

    [WireVar]
    public int EffectAmplifier { get; set; }

    [Wire]
    public bool ShowParticles { get; set; }

    [WireVar]
    public int EffectDurationTicks { get; set; }

    [WireVar]
    public ulong Tick { get; set; }

    [Wire]
    public bool Ambient { get; set; }
}
