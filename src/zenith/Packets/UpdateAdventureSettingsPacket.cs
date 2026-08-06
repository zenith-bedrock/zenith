using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>UpdateAdventureSettings (0xBC) — LAN defaults with UpdateAbilities (ADR §37).</summary>
[GamePacket((int)ProtocolInfo.UPDATE_ADVENTURE_SETTINGS_PACKET)]
sealed partial class UpdateAdventureSettingsPacket : DataPacket
{
    [Wire]
    public bool NoPvM { get; set; }

    [Wire]
    public bool NoMvP { get; set; }

    [Wire]
    public bool ImmutableWorld { get; set; }

    [Wire]
    public bool ShowNameTags { get; set; }

    [Wire]
    public bool AutoJump { get; set; }

    public static UpdateAdventureSettingsPacket CreateLanDefaults() =>
        new()
        {
            NoPvM = false,
            NoMvP = false,
            ImmutableWorld = false,
            ShowNameTags = true,
            AutoJump = true
        };
}
