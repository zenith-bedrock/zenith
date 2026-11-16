using System.Collections.Frozen;

namespace Zenith.Gameplay.Permissions;

/// <summary>Who holds a node when nothing says otherwise.</summary>
enum PermissionDefault { Everyone, Operator, Nobody }

/// <summary>
/// The exact permission nodes the server knows and their defaults. Filled during composition, then frozen: an unregistered node is
/// never granted, and configuration naming one is rejected instead of silently ignored.
/// </summary>
sealed class PermissionRegistry
{
    private readonly Dictionary<string, PermissionDefault> _nodes = new(StringComparer.Ordinal);
    private FrozenDictionary<string, PermissionDefault>? _frozen;

    public IReadOnlyCollection<string> Nodes => _frozen is not null ? _frozen.Keys : _nodes.Keys;

    public void Register(string node, PermissionDefault defaultFor)
    {
        if (_frozen is not null)
            throw new InvalidOperationException("PermissionRegistry is frozen; register before Freeze().");
        if (!IsWellFormed(node))
            throw new ArgumentException($"'{node}' is not a valid permission node (lowercase words separated by dots).", nameof(node));
        if (!_nodes.TryAdd(node, defaultFor))
            throw new InvalidOperationException($"Permission node '{node}' is already registered.");
    }

    public void Freeze() => _frozen ??= _nodes.ToFrozenDictionary(StringComparer.Ordinal);

    public bool TryGetDefault(string node, out PermissionDefault defaultFor)
    {
        if (_frozen is not null) return _frozen.TryGetValue(node, out defaultFor);
        return _nodes.TryGetValue(node, out defaultFor);
    }

    /// <summary>Nodes are exact: lowercase letters, digits and hyphens in dot-separated segments. No wildcards.</summary>
    public static bool IsWellFormed(string? node)
    {
        if (string.IsNullOrEmpty(node)) return false;
        foreach (var segment in node.Split('.'))
        {
            if (segment.Length == 0) return false;
            foreach (var c in segment)
                if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')) return false;
        }

        return true;
    }
}
