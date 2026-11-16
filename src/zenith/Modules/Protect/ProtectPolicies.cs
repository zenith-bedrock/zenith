using Zenith.Gameplay.Permissions;
using Zenith.Gameplay.Policy;

namespace Zenith.Modules.Protect;

/// <summary>Refuses any block edit that touches a protected cell, whole edit at a time, unless the actor may bypass protection.</summary>
sealed class ProtectBlockEditPolicy(ProtectRegions regions, PermissionAuthority authority) : IBlockEditPolicy
{
    public PolicyDecision Evaluate(in BlockEditProposal proposal)
    {
        if (regions.Snapshot.IsEmpty) return PolicyDecision.Allow;
        foreach (var cell in proposal.Cells)
        {
            if (!regions.TryFind(cell.X, cell.Y, cell.Z, out var region)) continue;
            // Asked only when a region is actually hit, so ordinary building never pays for the permission lookup.
            return authority.Has(proposal.Actor, ProtectNodes.Bypass)
                ? PolicyDecision.Allow
                : PolicyDecision.Deny($"This area is protected ({region.Name}).");
        }

        return PolicyDecision.Allow;
    }
}

/// <summary>
/// Keeps the environment out of a protected cell: no explosion, liquid, fire, falling block, support loss or growth changes it. There is
/// no actor and so no bypass. A region resists every cause unless it was told to let one through (<c>/protect allow</c>), one cause
/// at a time: letting saplings grow never lets leaves decay.
/// </summary>
sealed class ProtectEnvironmentPolicy(ProtectRegions regions) : IEnvironmentChangePolicy
{
    public PolicyDecision Evaluate(in EnvironmentChangeProposal proposal)
    {
        if (regions.Snapshot.IsEmpty) return PolicyDecision.Allow;
        var cell = proposal.Cell;
        return regions.TryFindResisting(cell.X, cell.Y, cell.Z, proposal.Cause, out var region)
            ? PolicyDecision.Deny($"This area is protected ({region.Name}).")
            : PolicyDecision.Allow;
    }
}

/// <summary>Keeps containers inside a protected region closed and untouchable (opening and moving items), either half of a double chest counting.</summary>
sealed class ProtectContainerAccessPolicy(ProtectRegions regions, PermissionAuthority authority) : IContainerAccessPolicy
{
    public PolicyDecision Evaluate(in ContainerAccessProposal proposal)
    {
        if (regions.Snapshot.IsEmpty) return PolicyDecision.Allow;
        if (!TryFindProtected(proposal, out var region)) return PolicyDecision.Allow;
        return authority.Has(proposal.Actor, ProtectNodes.Bypass)
            ? PolicyDecision.Allow
            : PolicyDecision.Deny($"This container is protected ({region.Name}).");
    }

    private bool TryFindProtected(in ContainerAccessProposal proposal, out ProtectRegion region)
    {
        if (regions.TryFind(proposal.Primary.X, proposal.Primary.Y, proposal.Primary.Z, out region)) return true;
        if (proposal.Partner is { } partner) return regions.TryFind(partner.X, partner.Y, partner.Z, out region);
        return false;
    }
}
