using System.Globalization;
using Zenith.Player;

namespace Zenith.Gameplay.Commands;

/// <summary>Which pool and rule a selector starts from: the Bedrock target selector letters, plus a plain player name.</summary>
enum SelectorKind
{
    /// <summary><c>@s</c>: whoever ran the command.</summary>
    Self,
    /// <summary><c>@p</c>: the nearest player.</summary>
    NearestPlayer,
    /// <summary><c>@a</c>: every player.</summary>
    AllPlayers,
    /// <summary><c>@e</c>: every entity, players included.</summary>
    AllEntities,
    /// <summary><c>@r</c>: a random player (or a random entity when a <c>type</c> is given).</summary>
    Random,
    /// <summary>A plain name: the online player called that.</summary>
    Named
}

/// <summary>A value that may be negated (<c>type=!zombie</c>).</summary>
readonly record struct SelectorMatch(string Value, bool Negated);

/// <summary>
/// A parsed Bedrock target selector (<c>@e[type=zombie,r=20,c=3]</c>), or a plain player name. Nothing here knows the world: it is the
/// request, and <see cref="SelectorResolver"/> applies it to the entities that exist when the command runs. Supported arguments:
/// <c>type</c>, <c>name</c>, <c>family</c>-less filters (<c>m</c> game mode, <c>l</c>/<c>lm</c> experience levels), distance
/// (<c>r</c>, <c>rm</c>), origin (<c>x</c>, <c>y</c>, <c>z</c>) and volume (<c>dx</c>, <c>dy</c>, <c>dz</c>), and <c>c</c> (count, negative takes
/// the farthest). Anything else is refused when parsed, so a typo never silently selects everything.
/// </summary>
sealed class TargetSelector
{
    public SelectorKind Kind { get; private init; }
    public string Text { get; private init; } = "";
    public string? PlayerName { get; private init; }
    public IReadOnlyList<SelectorMatch> Types { get; private init; } = [];
    public IReadOnlyList<SelectorMatch> Names { get; private init; } = [];
    public IReadOnlyList<(GameMode Mode, bool Negated)> Modes { get; private init; } = [];
    public float? MaxDistance { get; private init; }
    public float? MinDistance { get; private init; }
    /// <summary>Whether x, y, z are offsets from where the command was run (<c>x=~5</c>) and not absolute.</summary>
    public bool RelativeX { get; private init; }
    public bool RelativeY { get; private init; }
    public bool RelativeZ { get; private init; }
    public float? X { get; private init; }
    public float? Y { get; private init; }
    public float? Z { get; private init; }
    public float? Dx { get; private init; }
    public float? Dy { get; private init; }
    public float? Dz { get; private init; }
    public int? Count { get; private init; }
    public int? MaxLevel { get; private init; }
    public int? MinLevel { get; private init; }

    /// <summary>True when any of <c>dx</c>, <c>dy</c>, <c>dz</c> is given: the selector then asks about a box and not a point.</summary>
    public bool HasVolume => Dx is not null || Dy is not null || Dz is not null;

    public override string ToString() => Text;

    public static TargetSelector Named(string name) => new() { Kind = SelectorKind.Named, Text = name, PlayerName = name };

    /// <summary>Parses a token: <c>@s</c>, <c>@p</c>, <c>@a</c>, <c>@e</c>, <c>@r</c> with optional <c>[arguments]</c>, or a plain player name.</summary>
    public static bool TryParse(string token, out TargetSelector selector, out string error)
    {
        selector = null!;
        error = "";
        if (string.IsNullOrWhiteSpace(token)) { error = "Empty target."; return false; }
        if (token[0] != '@') { selector = Named(token); return true; }
        if (token.Length < 2) { error = "A selector needs a letter after @."; return false; }

        var kind = char.ToLowerInvariant(token[1]) switch
        {
            's' => SelectorKind.Self,
            'p' => SelectorKind.NearestPlayer,
            'a' => SelectorKind.AllPlayers,
            'e' => SelectorKind.AllEntities,
            'r' => SelectorKind.Random,
            _ => (SelectorKind?)null
        };
        if (kind is null) { error = $"Unknown selector '@{token[1]}'."; return false; }

        var rest = token[2..];
        var arguments = new List<(string Key, string Value)>();
        if (rest.Length > 0)
        {
            if (rest[0] != '[' || rest[^1] != ']') { error = "Selector arguments go in [brackets]."; return false; }
            foreach (var part in SplitArguments(rest[1..^1]))
            {
                var equals = part.IndexOf('=');
                if (equals <= 0 || equals == part.Length - 1) { error = $"'{part.Trim()}' is not key=value."; return false; }
                arguments.Add((part[..equals].Trim().ToLowerInvariant(), part[(equals + 1)..].Trim()));
            }
        }

        var types = new List<SelectorMatch>();
        var names = new List<SelectorMatch>();
        var modes = new List<(GameMode, bool)>();
        float? r = null, rm = null, x = null, y = null, z = null, dx = null, dy = null, dz = null;
        int? count = null, l = null, lm = null;
        bool relX = false, relY = false, relZ = false;
        foreach (var (key, raw) in arguments)
        {
            var negated = raw.StartsWith('!');
            var value = Unquote(negated ? raw[1..] : raw);
            switch (key)
            {
                case "type":
                    if (value.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase)) value = value["minecraft:".Length..];
                    if (!negated && types.Any(t => !t.Negated)) { error = "Only one type may be required."; return false; }
                    types.Add(new SelectorMatch(value.ToLowerInvariant(), negated));
                    break;
                case "name":
                    if (!negated && names.Any(n => !n.Negated)) { error = "Only one name may be required."; return false; }
                    names.Add(new SelectorMatch(value, negated));
                    break;
                case "m":
                    if (!TryMode(value, out var mode)) { error = $"Unknown game mode '{value}'."; return false; }
                    modes.Add((mode, negated));
                    break;
                case "r": if (!TryNumber(value, out var rv) || rv < 0) { error = "r must be a number of 0 or more."; return false; } r = rv; break;
                case "rm": if (!TryNumber(value, out var rmv) || rmv < 0) { error = "rm must be a number of 0 or more."; return false; } rm = rmv; break;
                case "x": if (!TryCoordinate(value, out var xv, out relX)) { error = "x must be a number or ~offset."; return false; } x = xv; break;
                case "y": if (!TryCoordinate(value, out var yv, out relY)) { error = "y must be a number or ~offset."; return false; } y = yv; break;
                case "z": if (!TryCoordinate(value, out var zv, out relZ)) { error = "z must be a number or ~offset."; return false; } z = zv; break;
                case "dx": if (!TryNumber(value, out var dxv)) { error = "dx must be a number."; return false; } dx = dxv; break;
                case "dy": if (!TryNumber(value, out var dyv)) { error = "dy must be a number."; return false; } dy = dyv; break;
                case "dz": if (!TryNumber(value, out var dzv)) { error = "dz must be a number."; return false; } dz = dzv; break;
                case "c":
                    if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var cv) || cv == 0)
                    { error = "c must be a non-zero whole number."; return false; }
                    count = cv;
                    break;
                case "l": if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var lv)) { error = "l must be a whole number."; return false; } l = lv; break;
                case "lm": if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var lmv)) { error = "lm must be a whole number."; return false; } lm = lmv; break;
                default:
                    error = $"Unsupported selector argument '{key}'.";
                    return false;
            }
        }

        if (r is not null && rm is not null && rm > r) { error = "rm is larger than r."; return false; }
        if (l is not null && lm is not null && lm > l) { error = "lm is larger than l."; return false; }

        selector = new TargetSelector
        {
            Kind = kind.Value, Text = token, Types = types, Names = names, Modes = modes,
            MaxDistance = r, MinDistance = rm, X = x, Y = y, Z = z, Dx = dx, Dy = dy, Dz = dz,
            Count = count, MaxLevel = l, MinLevel = lm, RelativeX = relX, RelativeY = relY, RelativeZ = relZ
        };
        return true;
    }

    /// <summary>Commas split arguments; a comma inside quotes does not.</summary>
    private static IEnumerable<string> SplitArguments(string body)
    {
        var inQuotes = false;
        var start = 0;
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '"') inQuotes = !inQuotes;
            else if (body[i] == ',' && !inQuotes)
            {
                yield return body[start..i];
                start = i + 1;
            }
        }

        if (start <= body.Length && body[start..].Trim().Length > 0) yield return body[start..];
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    /// <summary>A number, <c>~</c> (here) or <c>~offset</c>.</summary>
    private static bool TryCoordinate(string text, out float value, out bool relative)
    {
        relative = text.StartsWith('~');
        if (!relative) return TryNumber(text, out value);
        value = 0f;
        return text.Length == 1 || TryNumber(text[1..], out value);
    }

    private static bool TryNumber(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);

    private static bool TryMode(string text, out GameMode mode)
    {
        switch (text.ToLowerInvariant())
        {
            case "survival" or "s" or "0": mode = GameMode.Survival; return true;
            case "creative" or "c" or "1": mode = GameMode.Creative; return true;
            default: mode = GameMode.Survival; return false;
        }
    }
}
