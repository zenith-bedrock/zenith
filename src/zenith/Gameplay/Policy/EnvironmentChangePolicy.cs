namespace Zenith.Gameplay.Policy;

/// <summary>
/// Why the world changes with no player behind it. One cause means one thing: allowing or vetoing one never releases another
/// (a region that lets saplings grow must not thereby let leaves decay).
/// </summary>
enum EnvironmentCause
{
    Explosion,
    FluidFlow,
    Fire,
    Gravity,
    SupportLoss,
    Growth,
    Decay,
    Spread
}

/// <summary>One cell, one cause. The unit of decision is a cell; there is no batch form.</summary>
readonly record struct EnvironmentChangeProposal(EnvironmentCause Cause, ulong Tick, BlockCellChange Cell);

/// <summary>
/// A synchronous veto over one environment-driven cell change, evaluated on the game loop before the writer acts. Same rules as
/// <see cref="IBlockEditPolicy"/>: quick, no I/O, no mutation, values only, an exception denies. There is no actor, so there is no
/// bypass: a protected cell is protected from every cause.
/// <para/>
/// Granularity is the cell: an explosion beside a region carves what is outside it and leaves what is inside. A system whose unit of
/// change is a planned multi-cell structure (tree growth) asks about every cell while planning and abandons the whole unit on one veto.
/// </summary>
interface IEnvironmentChangePolicy
{
    PolicyDecision Evaluate(in EnvironmentChangeProposal proposal);
}

/// <summary>The policies consulted before an environment-driven change, in registration order; the first denial wins. Frozen after composition.</summary>
sealed class EnvironmentChangePolicies(Zenith.Logging.ILogger? logger = null)
{
    private readonly List<IEnvironmentChangePolicy> _policies = [];
    private readonly PolicyFaultLog _faults = new(logger, "Environment change policy", "denying the change");
    private IEnvironmentChangePolicy[]? _frozen;

    public void Register(IEnvironmentChangePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (_frozen is not null) throw new InvalidOperationException("EnvironmentChangePolicies is frozen; register before Freeze().");
        _policies.Add(policy);
    }

    public void Freeze() => _frozen ??= [.. _policies];

    public int Count => (_frozen ?? [.. _policies]).Length;

    public PolicyDecision Evaluate(in EnvironmentChangeProposal proposal)
    {
        foreach (var policy in _frozen ?? [.. _policies])
        {
            PolicyDecision decision;
            try { decision = policy.Evaluate(proposal); }
            catch (Exception ex)
            {
                // Fail closed: a policy that cannot answer must not let the world change.
                _faults.Report(policy, ex);
                return PolicyDecision.Deny("Policy error.");
            }

            if (!decision.IsAllowed) return decision;
        }

        return PolicyDecision.Allow;
    }

    /// <summary>Whether the change is permitted; the reason is for callers that log or tell someone, writers usually only need this.</summary>
    public bool Allows(EnvironmentCause cause, ulong tick, int x, int y, int z, int before, int after) =>
        Evaluate(new EnvironmentChangeProposal(cause, tick, new BlockCellChange(x, y, z, before, after))).IsAllowed;
}

/// <summary>
/// Keeps the log of a failing policy readable. Environment policies are asked per cell per tick, so a policy that throws would write
/// thousands of lines a second; this logs the first few failures of each policy type, then one line per thousand.
/// </summary>
sealed class PolicyFaultLog(Zenith.Logging.ILogger? logger, string setName, string consequence)
{
    private const int LoggedInFull = 3;
    private const int Every = 1000;

    private readonly Dictionary<Type, long> _failures = [];

    public void Report(object policy, Exception exception)
    {
        if (logger is null) return;

        long count;
        lock (_failures)
        {
            _failures.TryGetValue(policy.GetType(), out count);
            _failures[policy.GetType()] = ++count;
        }

        if (count <= LoggedInFull || count % Every == 0)
            logger.Error($"{setName} {policy.GetType().Name} failed ({count} so far); {consequence}: {exception.Message}");
    }
}
