using Zenith.Gameplay.Permissions;
using Zenith.Gameplay.Policy;

namespace Zenith.Server.Composition;

/// <summary>Builds the server's permission authority: the built-in nodes, frozen, plus the policy the configuration describes.</summary>
static class PermissionsComposition
{
    public static PermissionAuthority Create(ServerConfig config)
    {
        var registry = new PermissionRegistry();
        PermissionNodes.RegisterBuiltIns(registry);
        Zenith.Modules.Protect.ProtectNodes.Register(registry);
        registry.Freeze();
        return new PermissionAuthority(registry, PermissionPolicy.Create(
            registry,
            config.Permissions.Operators,
            config.Permissions.Grants,
            config.Permissions.Denies,
            config.Permissions.TrustSelfAsserted));
    }

    /// <summary>The policies every player block edit passes through, permission first; modules add theirs during composition.</summary>
    public static BlockEditPolicies CreateBlockPolicies(PermissionAuthority permissions, Zenith.Logging.ILogger? logger)
    {
        var policies = new BlockEditPolicies(logger);
        policies.Register(new PermissionBlockEditPolicy(permissions));
        return policies;
    }

    /// <summary>The policies every container open/transfer passes through, permission first; modules add theirs during composition.</summary>
    public static ContainerAccessPolicies CreateContainerPolicies(PermissionAuthority permissions, Zenith.Logging.ILogger? logger)
    {
        var policies = new ContainerAccessPolicies(logger);
        policies.Register(new PermissionContainerAccessPolicy(permissions));
        return policies;
    }
}
