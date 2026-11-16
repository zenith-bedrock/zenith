using Zenith.Gameplay.Policy;
using Zenith.World;

namespace Zenith.Gameplay.Inventory;

/// <summary>
/// The owner-side bridge from an open chest view to the container-access policies: builds the value proposal and asks. Shared by
/// everything on the inventory side that opens or moves items through a container, so access has one definition. The inventory
/// system keeps <see cref="Tick"/> current.
/// </summary>
sealed class ContainerAccessGuard(ContainerAccessPolicies? policies)
{
    public ulong Tick { get; set; }

    public PolicyDecision Check(global::Zenith.Player.Player player, in OpenChestView view, ContainerAccessKind kind)
    {
        if (policies is null) return PolicyDecision.Allow;
        ContainerCell? partner = view.TryGetPartner(out var px, out var py, out var pz) ? new ContainerCell(px, py, pz) : null;
        return policies.Evaluate(new ContainerAccessProposal(
            player.Principal, kind, new ContainerCell(view.PrimaryX, view.PrimaryY, view.PrimaryZ), partner, Tick));
    }
}
