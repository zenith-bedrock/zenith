using Zenith.Gameplay.Permissions;

namespace Zenith.Gameplay.Policy;

enum ContainerAccessKind
{
    /// <summary>Opening the container window.</summary>
    Open,

    /// <summary>Moving items into or out of a container whose window is already open.</summary>
    Transfer
}

/// <summary>Where a container's storage sits in the world.</summary>
readonly record struct ContainerCell(int X, int Y, int Z);

/// <summary>
/// A player's attempt to reach a container: who, how, and every cell of it (a double chest has two). Values only, so a policy can
/// decide but cannot touch the container or the player.
/// </summary>
readonly record struct ContainerAccessProposal(
    Principal Actor,
    ContainerAccessKind Kind,
    ContainerCell Primary,
    ContainerCell? Partner,
    ulong Tick);

/// <summary>
/// A synchronous veto over container access, evaluated on the game loop. Same rules as <see cref="IBlockEditPolicy"/>: quick, no I/O,
/// no mutation, an exception denies. Access is judged again for every transfer, not only when the window opens, so a revoked right
/// takes effect on a window that is already open.
/// </summary>
interface IContainerAccessPolicy
{
    PolicyDecision Evaluate(in ContainerAccessProposal proposal);
}

/// <summary>The policies consulted before container access, in registration order; the first denial wins. Frozen after composition.</summary>
sealed class ContainerAccessPolicies(Zenith.Logging.ILogger? logger = null)
{
    private readonly List<IContainerAccessPolicy> _policies = [];
    private readonly PolicyFaultLog _faults = new(logger, "Container access policy", "denying access");
    private IContainerAccessPolicy[]? _frozen;

    public void Register(IContainerAccessPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (_frozen is not null) throw new InvalidOperationException("ContainerAccessPolicies is frozen; register before Freeze().");
        _policies.Add(policy);
    }

    public void Freeze() => _frozen ??= [.. _policies];

    public PolicyDecision Evaluate(in ContainerAccessProposal proposal)
    {
        foreach (var policy in _frozen ?? [.. _policies])
        {
            PolicyDecision decision;
            try { decision = policy.Evaluate(proposal); }
            catch (Exception ex)
            {
                _faults.Report(policy, ex);
                return PolicyDecision.Deny("Policy error.");
            }

            if (!decision.IsAllowed) return decision;
        }

        return PolicyDecision.Allow;
    }
}

/// <summary>Opening and moving items are permissions of their own.</summary>
sealed class PermissionContainerAccessPolicy(PermissionAuthority authority) : IContainerAccessPolicy
{
    public PolicyDecision Evaluate(in ContainerAccessProposal proposal)
    {
        var node = proposal.Kind == ContainerAccessKind.Open ? PermissionNodes.ContainerOpen : PermissionNodes.ContainerTransfer;
        return authority.Has(proposal.Actor, node) ? PolicyDecision.Allow : PolicyDecision.Deny("You do not have permission to use this container.");
    }
}
