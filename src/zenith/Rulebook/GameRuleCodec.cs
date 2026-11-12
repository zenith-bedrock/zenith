using Zenith.Binary;

namespace Zenith.Rulebook;

/// <summary>
/// The wire (and storage) form of a game rule list: <c>varuint count</c>, then per rule
/// <c>string name, bool editable, varuint type, value</c> where a bool is one byte and an int or float is four
/// little-endian bytes. Captured from stock BDS 1.26.63.1 (<c>start_game</c> embeds exactly the bytes of
/// <c>game_rules_changed</c>); the same bytes are what a world stores.
/// </summary>
static class GameRuleCodec
{
    public static void Write(ref BinaryStream writer, IReadOnlyList<GameRule> rules)
    {
        writer.WriteUnsignedVarInt(rules.Count);
        foreach (var rule in rules)
        {
            writer.WriteVarString(rule.Name);
            writer.WriteBool(rule.Editable);
            writer.WriteUnsignedVarInt((int)rule.Kind);
            if (rule.Kind == GameRuleKind.Bool)
                writer.WriteBool(rule.Bits != 0);
            else
                writer.WriteInt(rule.Bits, BinaryStream.Endianess.Little);
        }
    }

    public static List<GameRule> Read(ref BinaryStream reader)
    {
        var count = reader.ReadUnsignedVarInt();
        if (count is < 0 or > 4096) throw new InvalidDataException($"Implausible game rule count {count}.");
        var rules = new List<GameRule>(count);
        for (var i = 0; i < count; i++)
        {
            var name = reader.ReadVarString();
            var editable = reader.ReadBool();
            var kind = (GameRuleKind)reader.ReadUnsignedVarInt();
            rules.Add(kind switch
            {
                GameRuleKind.Bool => new GameRule(name, editable, kind, reader.ReadBool() ? 1 : 0),
                GameRuleKind.Int or GameRuleKind.Float => new GameRule(name, editable, kind, reader.ReadInt(BinaryStream.Endianess.Little)),
                _ => throw new InvalidDataException($"Game rule '{name}' has unknown type {(int)kind}."),
            });
        }

        return rules;
    }

    public static byte[] ToBytes(IReadOnlyList<GameRule> rules)
    {
        var writer = new BinaryStream();
        Write(ref writer, rules);
        return writer.GetBufferDisposing().ToArray();
    }

    public static List<GameRule> FromBytes(byte[] bytes)
    {
        var reader = new BinaryStream(bytes);
        try
        {
            var rules = Read(ref reader);
            if (!reader.IsEndOfFile) throw new InvalidDataException("Trailing bytes after the game rule list.");
            return rules;
        }
        catch (InvalidOperationException e)
        {
            // BinaryStream reports a truncated buffer as InvalidOperationException; callers get one error type.
            throw new InvalidDataException($"Truncated game rule list: {e.Message}", e);
        }
        finally
        {
            reader.Dispose();
        }
    }
}
