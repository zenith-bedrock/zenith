namespace Zenith.Gameplay.Commands;

/// <summary>Protocol-independent command grammar and validation. It has no packet dependency.</summary>
sealed class CommandCatalog
{
    private readonly Dictionary<string, CommandDefinition> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CommandDefinition> _definitions = [];
    private bool _frozen;

    public IReadOnlyList<CommandDefinition> Definitions => _definitions;

    public void Register(CommandDefinition definition)
    {
        if (_frozen)
            throw new InvalidOperationException("CommandCatalog is frozen; register before Freeze().");
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);
        if (_names.ContainsKey(definition.Name) || definition.Aliases.Any(_names.ContainsKey))
            throw new InvalidOperationException($"Duplicate command name or alias for '{definition.Name}'.");
        _definitions.Add(definition);
        _names.Add(definition.Name, definition);
        foreach (var alias in definition.Aliases) _names.Add(alias, definition);
    }

    /// <summary>
    /// Marks composition complete — same compose → freeze → gameplay-reads-only contract as
    /// <c>RecipeRegistry</c>/<c>CreativeCatalog</c> (ADR §133). Called once by
    /// <see cref="CommandRuntime"/>'s constructor right after registering its built-in commands.
    /// </summary>
    internal void Freeze() => _frozen = true;

    public CommandParseResult Parse(string line, CommandPermission permission)
    {
        var tokens = Tokenize(line);
        if (tokens.Length == 0) return CommandParseResult.Unknown();
        if (!_names.TryGetValue(tokens[0], out var definition)) return CommandParseResult.Unknown();
        if (permission < definition.RequiredPermission) return CommandParseResult.Denied(definition);
        var values = tokens[1..];
        foreach (var overload in definition.Overloads)
            if (overload.TryParse(values, out var arguments)) return CommandParseResult.Success(definition, overload, arguments);
        return CommandParseResult.Invalid(definition);
    }

    public IReadOnlyList<string> Suggest(string prefix) => _definitions.SelectMany(d => new[] { d.Name }.Concat(d.Aliases))
        .Where(name => name.StartsWith(prefix.TrimStart('/'), StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string[] Tokenize(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return [];
        var trimmed = line.Trim();
        if (trimmed.StartsWith('/')) trimmed = trimmed[1..];
        return trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

enum CommandPermission { Any, Operator }

sealed record CommandDefinition(string Name, string Description, CommandPermission RequiredPermission, IReadOnlyList<CommandOverload> Overloads, IReadOnlyList<string> Aliases)
{
    public CommandDefinition(string name, string description, CommandPermission permission, params CommandOverload[] overloads) : this(name, description, permission, overloads, []) { }
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
