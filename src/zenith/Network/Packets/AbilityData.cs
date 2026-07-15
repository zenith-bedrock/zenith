using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>
/// Shared AbilityData blob for UpdateAbilities and AddPlayer (ADR §37).
/// Wire-only — no Player / GameMode enum dependency.
/// </summary>
static class AbilityData
{
    public const float DefaultFlySpeed = 0.05f;
    public const float DefaultVerticalFlySpeed = 1.0f;
    public const float DefaultWalkSpeed = 0.1f;

    /// <summary>Golden Survival mask — byte-identity with pre-§37 WriteMinimalAbilities.</summary>
    public static uint ValuesForSurvival() =>
        AbilityBits.Bit(AbilityBits.Build) |
        AbilityBits.Bit(AbilityBits.Mine) |
        AbilityBits.Bit(AbilityBits.DoorsAndSwitches) |
        AbilityBits.Bit(AbilityBits.OpenContainers) |
        AbilityBits.Bit(AbilityBits.AttackPlayers) |
        AbilityBits.Bit(AbilityBits.AttackMobs) |
        AbilityBits.Bit(AbilityBits.WalkSpeed);

    /// <summary>Creative: Survival base + MayFly + InstantBuild + optional Flying (Zenith join seed).</summary>
    public static uint ValuesForCreative(bool flying = true)
    {
        var values =
            ValuesForSurvival() |
            AbilityBits.Bit(AbilityBits.MayFly) |
            AbilityBits.Bit(AbilityBits.InstantBuild);
        if (flying)
            values |= AbilityBits.Bit(AbilityBits.Flying);
        return values;
    }

    public static uint ValuesForGameMode(int wireGameMode, bool flying = true) =>
        wireGameMode == AbilityBits.WireGameModeCreative
            ? ValuesForCreative(flying)
            : ValuesForSurvival();

    public static void Write(ref BinaryStream writer, long uniqueId, uint values)
    {
        uint allSet = (1u << AbilityBits.Count) - 1;

        writer.WriteULong((ulong)uniqueId, BinaryStream.Endianess.Little);
        writer.WriteByte(AbilityBits.PlayerPermissionMember);
        writer.WriteByte(AbilityBits.CommandPermissionNormal);
        writer.WriteByte(AbilityBits.LayerCount); // layer count
        writer.WriteUShort(AbilityBits.LayerBase, BinaryStream.Endianess.Little);
        writer.WriteUInt(allSet, BinaryStream.Endianess.Little);
        writer.WriteUInt(values, BinaryStream.Endianess.Little);
        writer.WriteFloat(DefaultFlySpeed, BinaryStream.Endianess.Little);
        writer.WriteFloat(DefaultVerticalFlySpeed, BinaryStream.Endianess.Little);
        writer.WriteFloat(DefaultWalkSpeed, BinaryStream.Endianess.Little);
    }
}
