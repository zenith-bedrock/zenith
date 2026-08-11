using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>One UpdateAttributes entry (name + min/max/value/defaults — frozen HUD seed §34).</summary>
readonly record struct AttributeEntry(
    string Name,
    float Min,
    float Max,
    float Value,
    float DefaultMin,
    float DefaultMax,
    float Default);

/// <summary>
/// UpdateAttributes (0x1d) — spawn HUD attributes (ADR §34 / §40).
/// Health/hunger values come from domain; remaining attrs still constant seed.
/// </summary>
sealed class UpdateAttributesPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.UPDATE_ATTRIBUTES_PACKET;

    public ulong ActorRuntimeId { get; set; }
    public AttributeEntry[] Attributes { get; set; } = [];
    public ulong Tick { get; set; }

    /// <summary>Back-compat: full defaults at 20/20.</summary>
    public static UpdateAttributesPacket CreateFrozenDefaults(ulong actorRuntimeId) =>
        CreateDefaults(actorRuntimeId, health: 20f, hunger: 20f);

    /// <summary>Spawn/update attributes using Player vitals (ADR §40).</summary>
    public static UpdateAttributesPacket CreateDefaults(ulong actorRuntimeId, float health, float hunger) =>
        new()
        {
            ActorRuntimeId = actorRuntimeId,
            Tick = 0,
            Attributes =
            [
                Entry("minecraft:health", 0f, 20f, health),
                Entry("minecraft:movement", 0f, float.MaxValue, 0.1f),
                Entry("minecraft:player.hunger", 0f, 20f, hunger),
                Entry("minecraft:player.saturation", 0f, 20f, 20f),
                Entry("minecraft:player.exhaustion", 0f, 5f, 0f),
                Entry("minecraft:player.level", 0f, 24791f, 0f),
                Entry("minecraft:player.experience", 0f, 1f, 0f)
            ]
        };

    /// <summary>
    /// Focused health replication for an actor already known to the recipient. This is a packet
    /// convenience, not a generic attribute model.
    /// </summary>
    public static UpdateAttributesPacket CreateHealth(ulong actorRuntimeId, float health, float maximum) =>
        new()
        {
            ActorRuntimeId = actorRuntimeId,
            Tick = 0,
            Attributes = [Entry("minecraft:health", 0f, maximum, health)]
        };

    private static AttributeEntry Entry(string name, float min, float max, float value) =>
        new(name, min, max, value, min, max, value);

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUnsignedVarLong((long)ActorRuntimeId);
        writer.WriteUnsignedVarInt(Attributes.Length);
        foreach (var a in Attributes)
        {
            writer.WriteFloat(a.Min, BinaryStream.Endianess.Little);
            writer.WriteFloat(a.Max, BinaryStream.Endianess.Little);
            writer.WriteFloat(a.Value, BinaryStream.Endianess.Little);
            writer.WriteFloat(a.DefaultMin, BinaryStream.Endianess.Little);
            writer.WriteFloat(a.DefaultMax, BinaryStream.Endianess.Little);
            writer.WriteFloat(a.Default, BinaryStream.Endianess.Little);
            writer.WriteVarString(a.Name);
            writer.WriteUnsignedVarInt(0); // modifiers empty
        }

        writer.WriteUnsignedVarLong((long)Tick);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
