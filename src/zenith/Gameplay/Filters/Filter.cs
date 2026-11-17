namespace Zenith.Gameplay.Filters;

/// <summary>
/// A condition over a <see cref="FilterContext"/>, in the grammar of the BDS packs (<c>test</c>, <c>subject</c>, <c>operator</c>,
/// <c>value</c>, <c>domain</c>; groups <c>all_of</c>, <c>any_of</c>, <c>none_of</c>). A rule that used to be a hard-coded
/// <c>if</c> (spawn light, biome, difficulty, the sun on the undead) is data that is parsed and validated once, at load, by
/// <see cref="FilterParser"/> against a <see cref="FilterRegistry"/>.
/// </summary>
abstract class Filter
{
    /// <summary>The filter with no conditions: always true (an entry that lists none).</summary>
    public static readonly Filter Always = new FilterGroup(FilterGroupKind.AllOf, []);

    public abstract bool Matches(FilterContext context);
}

enum FilterGroupKind { AllOf, AnyOf, NoneOf }

enum FilterOperator { Equals, NotEquals, Less, LessOrEqual, Greater, GreaterOrEqual }

sealed class FilterGroup(FilterGroupKind kind, IReadOnlyList<Filter> children) : Filter
{
    public FilterGroupKind Kind { get; } = kind;
    public IReadOnlyList<Filter> Children { get; } = children;

    public override bool Matches(FilterContext context)
    {
        switch (Kind)
        {
            case FilterGroupKind.AllOf:
                foreach (var child in Children)
                    if (!child.Matches(context)) return false;
                return true;
            case FilterGroupKind.AnyOf:
                foreach (var child in Children)
                    if (child.Matches(context)) return true;
                return false;
            default:
                foreach (var child in Children)
                    if (child.Matches(context)) return false;
                return true;
        }
    }
}

/// <summary>One <c>test</c> with its operator, value and domain already checked against the registry.</summary>
sealed class FilterLeaf(FilterTestDefinition definition, string? domain, FilterOperator op, object value) : Filter
{
    public FilterTestDefinition Definition { get; } = definition;

    public override bool Matches(FilterContext context)
    {
        switch (Definition.Kind)
        {
            case FilterValueKind.Bool:
            {
                var same = Definition.Bool!(context, domain) == (bool)value;
                return op == FilterOperator.Equals ? same : !same;
            }
            case FilterValueKind.Text:
            {
                var same = Definition.Text!(context, domain, (string)value);
                return op == FilterOperator.Equals ? same : !same;
            }
            default:
            {
                var actual = Definition.Number!(context, domain);
                var expected = (double)value;
                return op switch
                {
                    FilterOperator.Equals => actual == expected,
                    FilterOperator.NotEquals => actual != expected,
                    FilterOperator.Less => actual < expected,
                    FilterOperator.LessOrEqual => actual <= expected,
                    FilterOperator.Greater => actual > expected,
                    _ => actual >= expected
                };
            }
        }
    }
}
