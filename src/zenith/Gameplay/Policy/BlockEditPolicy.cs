using Zenith.Gameplay.Permissions;

namespace Zenith.Gameplay.Policy;

/// <summary>The answer of a policy: allow, or deny with a reason a player or a log can read.</summary>
readonly record struct PolicyDecision(bool IsAllowed, string? Reason = null)
{
    public static readonly PolicyDecision Allow = new(true);

    public static PolicyDecision Deny(string reason) => new(false, reason);
}

enum BlockEditKind
{
    Place,
    Break,

    /// <summary>Using a block in place (opening or closing a door, trapdoor or gate): the cells change state, nothing is added or removed.</summary>
    Interact
}

/// <summary>One cell of an edit: where, what was there, what the edit leaves there.</summary>
readonly record struct BlockCellChange(int X, int Y, int Z, int Before, int After);

/// <summary>
/// A complete player block edit, planned and validated but not yet applied: who acts, and every cell it would change (the
/// clicked cell first, then the cells a coupled block takes along, such as the other half of a door). Policies see exactly this
/// and nothing else: no player, world, inventory or channel, so a policy can decide but cannot mutate or hold on to the domain.
/// </summary>
readonly ref struct BlockEditProposal(Principal actor, BlockEditKind kind, ulong tick, ReadOnlySpan<BlockCellChange> cells)
{
    public Principal Actor { get; } = actor;
    public BlockEditKind Kind { get; } = kind;
    public ulong Tick { get; } = tick;

    /// <summary>The cells the edit would change; the first is the clicked cell. Valid only during the evaluation.</summary>
    public ReadOnlySpan<BlockCellChange> Cells { get; } = cells;
}

/// <summary>
/// A synchronous veto over a planned block edit. It runs on the game loop before any effect (item consumed, cell written,
/// drop produced), must be quick, must not block, await, do I/O or start edits of its own, and an exception counts as a denial.
/// </summary>
interface IBlockEditPolicy
{
    PolicyDecision Evaluate(in BlockEditProposal proposal);
}

/// <summary>
/// The policies consulted before a player block edit, in registration order; the first denial wins. Registered during
/// composition, then frozen.
/// </summary>
sealed class BlockEditPolicies(Zenith.Logging.ILogger? logger = null)
{
    private readonly List<IBlockEditPolicy> _policies = [];
    private readonly PolicyFaultLog _faults = new(logger, "Block edit policy", "denying the edit");
    private IBlockEditPolicy[]? _frozen;

    public void Register(IBlockEditPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (_frozen is not null) throw new InvalidOperationException("BlockEditPolicies is frozen; register before Freeze().");
        _policies.Add(policy);
    }

    public void Freeze() => _frozen ??= [.. _policies];

    public int Count => (_frozen ?? [.. _policies]).Length;

    public PolicyDecision Evaluate(in BlockEditProposal proposal)
    {
        foreach (var policy in _frozen ?? [.. _policies])
        {
            PolicyDecision decision;
            try { decision = policy.Evaluate(proposal); }
            catch (Exception ex)
            {
                // Fail closed: a policy that cannot answer must not let the edit through.
                _faults.Report(policy, ex);
                return PolicyDecision.Deny("Policy error.");
            }

            if (!decision.IsAllowed) return decision;
        }

        return PolicyDecision.Allow;
    }
}

/// <summary>Place and break are permissions: a principal without the node cannot edit, whatever else is true.</summary>
sealed class PermissionBlockEditPolicy(PermissionAuthority authority) : IBlockEditPolicy
{
    public PolicyDecision Evaluate(in BlockEditProposal proposal)
    {
        var node = proposal.Kind switch
        {
            BlockEditKind.Place => PermissionNodes.WorldPlace,
            BlockEditKind.Break => PermissionNodes.WorldBreak,
            BlockEditKind.Interact => PermissionNodes.WorldInteract,
            _ => null
        };
        if (node is null) return PolicyDecision.Deny("Unknown kind of edit.");
        return authority.Has(proposal.Actor, node) ? PolicyDecision.Allow : PolicyDecision.Deny("You do not have permission to do that here.");
    }
}
