using System.Text.Json;

namespace Zenith.Gameplay.Filters;

/// <summary>
/// Reads a filter in the BDS grammar from JSON and checks it against a <see cref="FilterRegistry"/>: an unknown test, domain, operator,
/// a value of the wrong kind (or a text the test does not know) or an unsupported subject fails the load with the path of the offending
/// node, instead of becoming a rule that silently never matches. A list is an implicit <c>all_of</c>.
/// </summary>
sealed class FilterParser(FilterRegistry registry)
{
    private static readonly HashSet<string> LeafKeys = ["test", "subject", "operator", "value", "domain"];
    private static readonly string[] GroupKeys = ["all_of", "any_of", "none_of"];

    /// <summary>Parses <paramref name="node"/>; <paramref name="where"/> names it in errors (for example <c>"zombie: filters"</c>).</summary>
    public Filter Parse(JsonElement node, string where)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Array:
                return Group(FilterGroupKind.AllOf, node, where);
            case JsonValueKind.Object:
                return Object(node, where);
            default:
                throw Invalid(where, "a filter is an object or a list of filters");
        }
    }

    private Filter Object(JsonElement node, string where)
    {
        if (node.TryGetProperty("test", out _)) return Leaf(node, where);

        Filter? found = null;
        foreach (var property in node.EnumerateObject())
        {
            var kind = Array.IndexOf(GroupKeys, property.Name);
            if (kind < 0) throw Invalid(where, $"unknown key '{property.Name}' (a filter has 'test', or one of all_of, any_of, none_of)");
            if (found is not null) throw Invalid(where, "a filter holds one group; nest the others");
            if (property.Value.ValueKind != JsonValueKind.Array) throw Invalid($"{where}.{property.Name}", "a group is a list of filters");
            found = Group((FilterGroupKind)kind, property.Value, $"{where}.{property.Name}");
        }

        return found ?? throw Invalid(where, "an empty filter (use no filter at all instead)");
    }

    private Filter Group(FilterGroupKind kind, JsonElement list, string where)
    {
        var children = new List<Filter>();
        var index = 0;
        foreach (var child in list.EnumerateArray())
            children.Add(Parse(child, $"{where}[{index++}]"));
        if (children.Count == 0) throw Invalid(where, "a group has no filters");
        return new FilterGroup(kind, children);
    }

    private Filter Leaf(JsonElement node, string where)
    {
        foreach (var property in node.EnumerateObject())
            if (!LeafKeys.Contains(property.Name)) throw Invalid(where, $"unknown key '{property.Name}' in a test");

        var name = node.GetProperty("test").GetString() ?? throw Invalid(where, "'test' must be a string");
        if (!registry.TryGet(name, out var definition))
            throw Invalid(where, $"unknown test '{name}' (known: {string.Join(", ", registry.Names.Order())})");

        if (node.TryGetProperty("subject", out var subject) && subject.GetString() is not ("self" or null))
            throw Invalid(where, $"subject '{subject.GetString()}' is not supported (only 'self')");

        string? domain = null;
        if (node.TryGetProperty("domain", out var domainNode))
        {
            domain = domainNode.GetString();
            if (domain is null || !definition.Domains.Contains(domain))
                throw Invalid(where, $"test '{name}' has no domain '{domain}'" + (definition.Domains.Count == 0 ? " (it takes none)" : $" (it has: {string.Join(", ", definition.Domains.Order())})"));
        }

        var op = FilterOperator.Equals;
        if (node.TryGetProperty("operator", out var opNode))
        {
            var text = opNode.GetString();
            op = text switch
            {
                "equals" or "==" or "=" => FilterOperator.Equals,
                "not" or "not_equals" or "!=" or "<>" => FilterOperator.NotEquals,
                "<" => FilterOperator.Less,
                "<=" => FilterOperator.LessOrEqual,
                ">" => FilterOperator.Greater,
                ">=" => FilterOperator.GreaterOrEqual,
                _ => throw Invalid(where, $"unknown operator '{text}'")
            };
        }

        object value;
        switch (definition.Kind)
        {
            case FilterValueKind.Bool:
                if (!node.TryGetProperty("value", out var b)) value = true; // the pack's default for a bool test
                else if (b.ValueKind is JsonValueKind.True or JsonValueKind.False) value = b.GetBoolean();
                else throw Invalid(where, $"test '{name}' takes true or false");
                if (op is not (FilterOperator.Equals or FilterOperator.NotEquals)) throw Invalid(where, $"test '{name}' only compares with equals or not");
                break;
            case FilterValueKind.Number:
                if (!node.TryGetProperty("value", out var n) || n.ValueKind != JsonValueKind.Number) throw Invalid(where, $"test '{name}' takes a number");
                value = n.GetDouble();
                break;
            default:
                if (!node.TryGetProperty("value", out var t) || t.ValueKind != JsonValueKind.String) throw Invalid(where, $"test '{name}' takes a string");
                value = t.GetString()!;
                if (definition.ValidText is { } valid && !valid((string)value)) throw Invalid(where, $"test '{name}' does not know the value '{value}'");
                if (op is not (FilterOperator.Equals or FilterOperator.NotEquals)) throw Invalid(where, $"test '{name}' only compares with equals or not");
                break;
        }

        return new FilterLeaf(definition, domain, op, value);
    }

    private static InvalidDataException Invalid(string where, string message) => new($"{where}: {message}.");
}
