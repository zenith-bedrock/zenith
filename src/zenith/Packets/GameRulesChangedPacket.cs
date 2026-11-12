using Zenith.Binary;
using Zenith.Rulebook;

namespace Zenith.Packets;

/// <summary>GameRulesChanged (0x48) — the complete rule list; the same bytes <c>start_game</c> embeds (captured from BDS 1.26.63.1).</summary>
sealed class GameRulesChangedPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.GAME_RULES_CHANGED_PACKET;

    public IReadOnlyList<GameRule> Rules { get; set; } = [];

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        GameRuleCodec.Write(ref writer, Rules);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) => Rules = GameRuleCodec.Read(ref stream);
}
