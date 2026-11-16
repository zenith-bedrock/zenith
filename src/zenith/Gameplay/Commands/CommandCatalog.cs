namespace Zenith.Gameplay.Commands;

/// <summary>Protocol-independent command grammar and validation. It has no packet dependency.</summary>
sealed class CommandCatalog
{
    private readonly Dictionary<string, CommandDefinition> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CommandDefinition> _definitions = [];
    private bool _frozen;

    public IReadOnlyList<CommandDefinition> Definitions => _definitions;

    /// <summary>
    /// Adds a command. Everything is validated before the catalog changes, so a rejected definition leaves it exactly as it was.
    /// The catalog keeps its own copy of the definition's lists: later edits by the caller cannot change what it parses.
    /// </summary>
    public void Register(CommandDefinition definition)
    {
        if (_frozen)
            throw new InvalidOperationException("CommandCatalog is frozen; register before Freeze().");
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);
        if (definition.Permissions.Count == 0 || definition.Permissions.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException($"Command '{definition.Name}' must name at least one permission node.", nameof(definition));

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in definition.Aliases.Prepend(definition.Name))
        {
            if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsWhiteSpace))
                throw new ArgumentException($"Command '{definition.Name}' has an empty or whitespace-containing name or alias.", nameof(definition));
            if (!names.Add(name))
                throw new InvalidOperationException($"Command '{definition.Name}' repeats the name or alias '{name}'.");
            if (_names.ContainsKey(name))
                throw new InvalidOperationException($"Duplicate command name or alias '{name}' (registering '{definition.Name}').");
        }

        var owned = definition with
        {
            Permissions = [.. definition.Permissions],
            Aliases = [.. definition.Aliases],
            Overloads = [.. definition.Overloads]
        };
        _definitions.Add(owned);
        foreach (var name in names) _names.Add(name, owned);
    }

    /// <summary>
    /// Marks composition complete — same compose → freeze → gameplay-reads-only contract as
    /// <c>RecipeRegistry</c>/<c>CreativeCatalog</c> (ADR §133). Called once by
    /// <see cref="CommandRuntime"/>'s constructor right after registering its built-in commands.
    /// </summary>
    internal void Freeze() => _frozen = true;

    /// <summary>
    /// Parses a line for a sender. <paramref name="holds"/> says which permission nodes the sender has; a command is usable when the
    /// sender holds any node the definition lists, and is otherwise reported as denied (never as unknown, so a typo and a refusal differ).
    /// </summary>
    public CommandParseResult Parse(string line, Func<string, bool> holds)
    {
        var tokens = Tokenize(line);
        if (tokens.Length == 0) return CommandParseResult.Unknown();
        if (!_names.TryGetValue(tokens[0], out var definition)) return CommandParseResult.Unknown();
        if (!IsUsable(definition, holds)) return CommandParseResult.Denied(definition);
        var values = tokens[1..];
        foreach (var overload in definition.Overloads)
            if (overload.TryParse(values, out var arguments)) return CommandParseResult.Success(definition, overload, arguments);
        return CommandParseResult.Invalid(definition);
    }

    /// <summary>Whether a sender holding what <paramref name="holds"/> reports may use (and so should be shown) the command.</summary>
    public static bool IsUsable(CommandDefinition definition, Func<string, bool> holds) => definition.Permissions.Any(holds);

    /// <summary>Names and aliases starting with the prefix that the sender may use: discovery follows authorization.</summary>
    public IReadOnlyList<string> Suggest(string prefix, Func<string, bool> holds) => _definitions
        .Where(d => IsUsable(d, holds))
        .SelectMany(d => new[] { d.Name }.Concat(d.Aliases))
        .Where(name => name.StartsWith(prefix.TrimStart('/'), StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string[] Tokenize(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return [];
        var trimmed = line.Trim();
        if (trimmed.StartsWith('/')) trimmed = trimmed[1..];
        return trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

/// <summary>
/// A command and the permission nodes that unlock it: a sender holding <i>any</i> of <see cref="Permissions"/> may use it (finer
/// distinctions, like acting on others, are checked by the executor against its own node).
/// </summary>
sealed record CommandDefinition(string Name, string Description, IReadOnlyList<string> Permissions, IReadOnlyList<CommandOverload> Overloads, IReadOnlyList<string> Aliases)
{
    public CommandDefinition(string name, string description, string permission, params CommandOverload[] overloads) : this(name, description, [permission], overloads, []) { }
}

sealed record CommandOverload(IReadOnlyList<CommandArgument> Arguments)
{
    public CommandOverload(params CommandArgument[] arguments) : this((IReadOnlyList<CommandArgument>)arguments) { }
    public bool TryParse(IReadOnlyList<string> tokens, out IReadOnlyDictionary<string, object> values)
    {
        var parsed = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        values = parsed;
        if (tokens.Count > Arguments.Count) return false;
        for (var i = 0; i < Arguments.Count; i++)
        {
            var argument = Arguments[i];
            // Once tokens run out, EVERY remaining argument must be optional-or-fail — not just the
            // first one at exactly i == tokens.Count. The old `==` check only handled the boundary
            // token correctly and then indexed tokens[i] out of range for any argument past it,
            // throwing ArgumentOutOfRangeException instead of returning false for e.g. "/effect give
            // poison" (2 tokens, 5 arguments, 3 trailing optional).
            if (i >= tokens.Count) { if (!argument.Optional) return false; continue; }
            if (!argument.TryParse(tokens[i], out var value)) return false;
            parsed.Add(argument.Name, value);
        }
        return true;
    }
}

abstract record CommandArgument(string Name, bool Optional = false) { public abstract bool TryParse(string token, out object value); }
sealed record EnumCommandArgument(string Name, IReadOnlyDictionary<string, object> Values, bool Optional = false) : CommandArgument(Name, Optional)
{ public override bool TryParse(string token, out object value) => Values.TryGetValue(token, out value!); }
sealed record IntegerCommandArgument(string Name, int Minimum, int Maximum, bool Optional = false) : CommandArgument(Name, Optional)
{ public override bool TryParse(string token, out object value) { value = 0; if (!int.TryParse(token, out var parsed) || parsed < Minimum || parsed > Maximum) return false; value = parsed; return true; } }
sealed record StringCommandArgument(string Name, bool Optional = false) : CommandArgument(Name, Optional)
{ public override bool TryParse(string token, out object value) { value = token; return !string.IsNullOrWhiteSpace(token); } }
sealed record PlayerCommandArgument(string Name, Func<string, object?> Resolve, bool Optional = false) : CommandArgument(Name, Optional)
{ public override bool TryParse(string token, out object value) { value = Resolve(token)!; return value is not null; } }

enum CommandParseStatus { Success, Unknown, Denied, Invalid }
readonly record struct CommandParseResult(CommandParseStatus Status, CommandDefinition? Definition, CommandOverload? Overload, IReadOnlyDictionary<string, object>? Arguments)
{
    public static CommandParseResult Success(CommandDefinition definition, CommandOverload overload, IReadOnlyDictionary<string, object> arguments) => new(CommandParseStatus.Success, definition, overload, arguments);
    public static CommandParseResult Unknown() => new(CommandParseStatus.Unknown, null, null, null);
    public static CommandParseResult Denied(CommandDefinition definition) => new(CommandParseStatus.Denied, definition, null, null);
    public static CommandParseResult Invalid(CommandDefinition definition) => new(CommandParseStatus.Invalid, definition, null, null);
}
