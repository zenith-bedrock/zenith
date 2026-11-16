using System.Collections.Frozen;

namespace Zenith.Gameplay.Permissions;

/// <summary>
/// An immutable set of administrators, grants and denies keyed by canonical identity. It is a snapshot: replacing it is how a
/// reload or a runtime change takes effect, and readers on any thread see either the old one or the new one, never a mix.
/// </summary>
sealed class PermissionPolicy
{
    public static readonly PermissionPolicy Empty = new(
        FrozenSet<Guid>.Empty,
        FrozenDictionary<Guid, FrozenSet<string>>.Empty,
        FrozenDictionary<Guid, FrozenSet<string>>.Empty,
        trustSelfAsserted: false);

    private readonly FrozenSet<Guid> _operators;
    private readonly FrozenDictionary<Guid, FrozenSet<string>> _grants;
    private readonly FrozenDictionary<Guid, FrozenSet<string>> _denies;

    private PermissionPolicy(
        FrozenSet<Guid> operators,
        FrozenDictionary<Guid, FrozenSet<string>> grants,
        FrozenDictionary<Guid, FrozenSet<string>> denies,
        bool trustSelfAsserted)
    {
        _operators = operators;
        _grants = grants;
        _denies = denies;
        TrustSelfAsserted = trustSelfAsserted;
    }

    /// <summary>
    /// Development switch: let a stable self-asserted identity (self-signed / offline login) hold privilege. Off by default,
    /// because such an identity proves nothing: anyone can claim any id.
    /// </summary>
    public bool TrustSelfAsserted { get; }

    public IReadOnlyCollection<Guid> Operators => _operators.Items;

    public bool IsOperator(Guid subject) => _operators.Contains(subject);

    public bool IsGranted(Guid subject, string node) => _grants.TryGetValue(subject, out var set) && set.Contains(node);

    public bool IsDenied(Guid subject, string node) => _denies.TryGetValue(subject, out var set) && set.Contains(node);

    /// <summary>Whether durable privilege (operator status, grants) may apply to a principal of this trust.</summary>
    public bool CanHoldPrivilege(IdentityTrust trust) => trust switch
    {
        IdentityTrust.Authenticated => true,
        IdentityTrust.SelfAsserted => TrustSelfAsserted,
        _ => false
    };

    /// <summary>Builds a policy from textual configuration, rejecting anything it cannot vouch for rather than guessing.</summary>
    public static PermissionPolicy Create(
        PermissionRegistry registry,
        IEnumerable<string> operators,
        IReadOnlyDictionary<string, List<string>>? grants,
        IReadOnlyDictionary<string, List<string>>? denies,
        bool trustSelfAsserted)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var operatorIds = operators.Select(raw => ParseSubject(raw, "permissions.operators")).ToFrozenSet();
        return new PermissionPolicy(
            operatorIds,
            ParseNodes(registry, grants, "permissions.grants"),
            ParseNodes(registry, denies, "permissions.denies"),
            trustSelfAsserted);
    }

    private static FrozenDictionary<Guid, FrozenSet<string>> ParseNodes(
        PermissionRegistry registry, IReadOnlyDictionary<string, List<string>>? source, string where)
    {
        if (source is null || source.Count == 0) return FrozenDictionary<Guid, FrozenSet<string>>.Empty;
        var result = new Dictionary<Guid, FrozenSet<string>>();
        foreach (var (rawSubject, nodes) in source)
        {
            var subject = ParseSubject(rawSubject, where);
            var list = nodes ?? [];
            foreach (var node in list)
            {
                if (!registry.TryGetDefault(node, out _))
                    throw new InvalidOperationException($"{where} names unknown permission node '{node}'.");
            }

            result[subject] = (result.TryGetValue(subject, out var existing) ? existing.Concat(list) : list)
                .ToFrozenSet(StringComparer.Ordinal);
        }

        return result.ToFrozenDictionary();
    }

    private static Guid ParseSubject(string? raw, string where)
    {
        if (!Guid.TryParse(raw, out var subject) || subject == Guid.Empty)
            throw new InvalidOperationException($"{where} entry '{raw}' is not an identity UUID.");
        return subject;
    }
}
