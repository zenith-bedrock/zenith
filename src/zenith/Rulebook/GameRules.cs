namespace Zenith.Rulebook;

/// <summary>
/// The world's game rules: a fixed, ordered set (names, types and defaults come from the stock BDS capture, see
/// <see cref="GameRuleDefaults"/>) whose values can change at runtime. Reads are lock-free and safe from any thread:
/// every change publishes a new immutable snapshot. <see cref="Version"/> grows with every change so a system can
/// notice it and replicate.
///
/// What each rule does server-side is NOT implied by its presence: every rule is replicated to the client (which
/// reads some of them itself, e.g. <c>showCoordinates</c>), but only those with a typed accessor here are enforced
/// by Zenith's gameplay. docs/bds-differential-join.md lists which.
/// </summary>
sealed class GameRules
{
    public const string KeepInventoryName = "keepInventory";
    public const string FallDamageName = "fallDamage";
    public const string NaturalRegenerationName = "naturalRegeneration";
    public const string DoTileDropsName = "doTileDrops";
    public const string DoMobSpawningName = "doMobSpawning";

    private sealed record State(long Version, GameRule[] Rules);

    private readonly Dictionary<string, int> _index;
    private readonly object _writeGate = new();
    private volatile State _state;

    public GameRules(IEnumerable<GameRule> defaults)
    {
        var rules = defaults.ToArray();
        _index = new Dictionary<string, int>(rules.Length, StringComparer.Ordinal);
        for (var i = 0; i < rules.Length; i++)
            if (!_index.TryAdd(rules[i].Name, i))
                throw new ArgumentException($"Duplicate game rule '{rules[i].Name}'.", nameof(defaults));
        _state = new State(0, rules);

        // Fail at composition, not at the first tick: every rule a typed accessor reads must exist with its type.
        _ = (KeepInventory, FallDamage, NaturalRegeneration, DoTileDrops, DoMobSpawning);
    }

    /// <summary>Every rule in wire order (the order BDS sends them).</summary>
    public IReadOnlyList<GameRule> Snapshot => _state.Rules;

    public long Version => _state.Version;

    public bool KeepInventory => Get(KeepInventoryName).AsBool;

    public bool FallDamage => Get(FallDamageName).AsBool;

    public bool NaturalRegeneration => Get(NaturalRegenerationName).AsBool;

    public bool DoTileDrops => Get(DoTileDropsName).AsBool;

    public bool DoMobSpawning => Get(DoMobSpawningName).AsBool;

    public GameRule Get(string name) =>
        TryGet(name, out var rule) ? rule : throw new KeyNotFoundException($"Unknown game rule '{name}'.");

    public bool TryGet(string name, out GameRule rule)
    {
        var state = _state;
        if (_index.TryGetValue(name, out var i))
        {
            rule = state.Rules[i];
            return true;
        }

        rule = default;
        return false;
    }

    /// <summary>Changes a rule from user text. Fails (with a reason) for an unknown name, a rule the world does not allow to be edited, or a value of the wrong type.</summary>
    public bool TrySet(string name, string text, out string error)
    {
        lock (_writeGate)
        {
            var state = _state;
            if (!_index.TryGetValue(name, out var i))
            {
                error = $"Unknown game rule '{name}'.";
                return false;
            }

            var current = state.Rules[i];
            if (!current.Editable)
            {
                error = $"Game rule '{name}' cannot be changed.";
                return false;
            }

            if (!current.TryWithValue(text, out var updated))
            {
                error = $"'{text}' is not a valid {current.Kind.ToString().ToLowerInvariant()} for game rule '{name}'.";
                return false;
            }

            error = "";
            if (updated == current) return true;
            var rules = (GameRule[])state.Rules.Clone();
            rules[i] = updated;
            _state = new State(state.Version + 1, rules);
            return true;
        }
    }

    /// <summary>
    /// Applies stored values on top of the defaults. Only rules this build knows, with the same type, are taken; the
    /// rest are returned in <paramref name="ignored"/> so a caller can say what was dropped instead of dropping it silently.
    /// </summary>
    public void Restore(IEnumerable<GameRule> stored, out IReadOnlyList<string> ignored)
    {
        var skipped = new List<string>();
        lock (_writeGate)
        {
            var rules = (GameRule[])_state.Rules.Clone();
            var changed = false;
            foreach (var rule in stored)
            {
                if (!_index.TryGetValue(rule.Name, out var i) || rules[i].Kind != rule.Kind)
                {
                    skipped.Add(rule.Name);
                    continue;
                }

                var merged = rules[i] with { Bits = rule.Bits };
                if (merged == rules[i]) continue;
                rules[i] = merged;
                changed = true;
            }

            if (changed) _state = new State(_state.Version + 1, rules);
        }

        ignored = skipped;
    }
}
