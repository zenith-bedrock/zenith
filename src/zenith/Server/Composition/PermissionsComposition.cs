using Zenith.Gameplay.Permissions;

namespace Zenith.Server.Composition;

/// <summary>Builds the server's permission authority: the built-in nodes, frozen, plus the policy the configuration describes.</summary>
static class PermissionsComposition
{
    public static PermissionAuthority Create(ServerConfig config)
    {
        var registry = new PermissionRegistry();
        PermissionNodes.RegisterBuiltIns(registry);
        registry.Freeze();
        return new PermissionAuthority(registry, PermissionPolicy.Create(
            registry,
            config.Permissions.Operators,
            config.Permissions.Grants,
            config.Permissions.Denies,
            config.Permissions.TrustSelfAsserted));
    }
}
