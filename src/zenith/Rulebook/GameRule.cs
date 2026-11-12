using System.Globalization;

namespace Zenith.Rulebook;

/// <summary>The three value types Bedrock game rules have; the numbers are the wire ids.</summary>
enum GameRuleKind : byte
{
    Bool = 1,
    Int = 2,
    Float = 3,
}

/// <summary>
/// One game rule: its name, whether a player may change it, and a typed value. The value lives in 32 bits (bool = 0/1,
/// int, or the bits of a float) so the struct stays flat and comparable.
/// </summary>
readonly record struct GameRule(string Name, bool Editable, GameRuleKind Kind, int Bits)
{
    public static GameRule OfBool(string name, bool value, bool editable = true) =>
        new(name, editable, GameRuleKind.Bool, value ? 1 : 0);

    public static GameRule OfInt(string name, int value, bool editable = true) =>
        new(name, editable, GameRuleKind.Int, value);

    public static GameRule OfFloat(string name, float value, bool editable = true) =>
        new(name, editable, GameRuleKind.Float, BitConverter.SingleToInt32Bits(value));

    public bool AsBool => Kind == GameRuleKind.Bool ? Bits != 0 : throw WrongKind(GameRuleKind.Bool);

    public int AsInt => Kind == GameRuleKind.Int ? Bits : throw WrongKind(GameRuleKind.Int);

    public float AsFloat => Kind == GameRuleKind.Float ? BitConverter.Int32BitsToSingle(Bits) : throw WrongKind(GameRuleKind.Float);

    /// <summary>Same rule with the value read from <paramref name="text"/> (<c>true</c>/<c>false</c>, an integer, a float).</summary>
    public bool TryWithValue(string text, out GameRule updated)
    {
        updated = this;
        switch (Kind)
        {
            case GameRuleKind.Bool when bool.TryParse(text, out var b):
                updated = this with { Bits = b ? 1 : 0 };
                return true;
            case GameRuleKind.Int when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i):
                updated = this with { Bits = i };
                return true;
            case GameRuleKind.Float when float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && float.IsFinite(f):
                updated = this with { Bits = BitConverter.SingleToInt32Bits(f) };
                return true;
            default:
                return false;
        }
    }

    public string ValueText => Kind switch
    {
        GameRuleKind.Bool => Bits != 0 ? "true" : "false",
        GameRuleKind.Int => Bits.ToString(CultureInfo.InvariantCulture),
        _ => BitConverter.Int32BitsToSingle(Bits).ToString(CultureInfo.InvariantCulture),
    };

    private InvalidOperationException WrongKind(GameRuleKind wanted) =>
        new($"Game rule '{Name}' is {Kind}, not {wanted}.");
}
