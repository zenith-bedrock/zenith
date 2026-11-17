using System.Reflection;

namespace Zenith.Gameplay.Runtime;

/// <summary>
/// Names a game system once, on the class: the name its tick timing is published under (<c>tick.system.&lt;name&gt;</c>) and the
/// one <see cref="Server.ServerServices.Register(IGameSystem)"/> reads. Adding a system needs this attribute and one registration
/// line; the diagnostics layout (which is fixed before the first tick) is derived from every class that carries it.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
sealed class GameSystemAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    /// <summary>The name of a system's class, or null when the class is not a named game system.</summary>
    public static string? NameOf(Type type) => type.GetCustomAttribute<GameSystemAttribute>(inherit: false)?.Name;

    /// <summary>Every named game system in this assembly, in name order (the diagnostics layout is built from it).</summary>
    public static IReadOnlyList<string> AllNames { get; } = typeof(GameSystemAttribute).Assembly.GetTypes()
        .Select(NameOf).OfType<string>().Order(StringComparer.Ordinal).ToArray();
}
