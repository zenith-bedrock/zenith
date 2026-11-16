namespace Zenith.Gameplay.Permissions;

/// <summary>Why a permission check came out the way it did. Useful for logs and tests; callers normally only need <see cref="PermissionAuthority.Has"/>.</summary>
enum PermissionDecision
{
    Granted,
    GrantedByDefault,
    GrantedAsOperator,
    DeniedUnknownNode,
    DeniedExplicit,
    DeniedByDefault,
    DeniedNotOperator
}

/// <summary>
/// Answers "may this principal do this?". Precedence: unknown node denies; an explicit deny beats an explicit grant, which beats
/// the node's default. Durable privilege (operator status, grants) applies only to identities the policy trusts; denies and
/// <see cref="PermissionDefault.Everyone"/> defaults apply to anyone. Safe to read from any thread; the policy is swapped whole.
/// </summary>
sealed class PermissionAuthority
{
    private readonly PermissionRegistry _registry;
    private PermissionPolicy _policy;

    public PermissionAuthority(PermissionRegistry registry, PermissionPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Freeze();
        _registry = registry;
        _policy = policy ?? PermissionPolicy.Empty;
    }

    public PermissionRegistry Registry => _registry;

    public PermissionPolicy Policy => Volatile.Read(ref _policy);

    /// <summary>Installs a new policy (reload / grant change). Takes effect for every check made after this returns.</summary>
    public void Replace(PermissionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Volatile.Write(ref _policy, policy);
    }

    public bool Has(in Principal principal, string node) => Evaluate(principal, node) is
        PermissionDecision.Granted or PermissionDecision.GrantedByDefault or PermissionDecision.GrantedAsOperator;

    public PermissionDecision Evaluate(in Principal principal, string node)
    {
        if (!_registry.TryGetDefault(node, out var defaultFor)) return PermissionDecision.DeniedUnknownNode;

        var policy = Policy;
        if (policy.IsDenied(principal.Subject, node)) return PermissionDecision.DeniedExplicit;

        var privileged = policy.CanHoldPrivilege(principal.Trust);
        if (privileged && policy.IsGranted(principal.Subject, node)) return PermissionDecision.Granted;

        return defaultFor switch
        {
            PermissionDefault.Everyone => PermissionDecision.GrantedByDefault,
            PermissionDefault.Operator => privileged && policy.IsOperator(principal.Subject)
                ? PermissionDecision.GrantedAsOperator
                : PermissionDecision.DeniedNotOperator,
            _ => PermissionDecision.DeniedByDefault
        };
    }
}
