using System.Reflection;
using System.Text.Json;
using Zenith.Gameplay.Filters;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// Behaviour rules that depend on where a mob stands, as filters from the embedded <c>data/entity_rules.json</c>: today the sun on the
/// undead. Validated at load like the spawn table.
/// </summary>
sealed class EntityRules
{
    public const string ResourceName = "entity_rules.json";

    private static readonly Lazy<EntityRules> Loaded = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        return Parse(stream);
    });

    public static EntityRules Default => Loaded.Value;

    /// <summary>When an undead mob catches fire from the sun.</summary>
    public Filter BurnsInDaylight { get; }

    private EntityRules(Filter burnsInDaylight) => BurnsInDaylight = burnsInDaylight;

    public static EntityRules Parse(Stream json, FilterRegistry? registry = null)
    {
        using var doc = JsonDocument.Parse(json);
        var parser = new FilterParser(registry ?? FilterRegistry.Default);
        try
        {
            return new EntityRules(parser.Parse(doc.RootElement.GetProperty("burns_in_daylight"), "burns_in_daylight"));
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"{ResourceName}: {ex.Message}");
        }
    }
}
