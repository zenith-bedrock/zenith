using Zenith.Gameplay.Commands;
using Zenith.Gameplay.Permissions;
using Zenith.Gameplay.Policy;

namespace Zenith.Modules.Protect;

/// <summary>The permission nodes the module owns.</summary>
static class ProtectNodes
{
    /// <summary>Edit and use containers inside a protected region.</summary>
    public const string Bypass = "zenith.protect.bypass";

    /// <summary>Create, remove and list regions.</summary>
    public const string Manage = "zenith.protect.manage";

    public static void Register(PermissionRegistry registry)
    {
        registry.Register(Bypass, PermissionDefault.Operator);
        registry.Register(Manage, PermissionDefault.Operator);
    }
}

/// <summary>
/// Region protection, built only on the internal extension seams: permission nodes, block-edit and container-access policies, and a
/// bound command. It sees values (principals, cells), never a player, world, inventory, packet or session, so everything it does
/// is decided before an effect and can be tested headless.
/// </summary>
sealed class ProtectModule
{
    private const int CoordinateLimit = 30_000_000;

    public ProtectRegions Regions { get; } = new();

    public IBlockEditPolicy BlockPolicy { get; }

    public IContainerAccessPolicy ContainerPolicy { get; }

    public CommandBinding Command { get; }

    public ProtectModule(PermissionAuthority authority)
    {
        BlockPolicy = new ProtectBlockEditPolicy(Regions, authority);
        ContainerPolicy = new ProtectContainerAccessPolicy(Regions, authority);
        Command = CreateCommand();
    }

    /// <summary>Builds the module and installs it into the policy sets; call before they freeze.</summary>
    public static ProtectModule Compose(PermissionAuthority authority, BlockEditPolicies blocks, ContainerAccessPolicies containers)
    {
        var module = new ProtectModule(authority);
        module.Install(blocks, containers);
        return module;
    }

    /// <summary>Adds the module's vetoes. Called during composition, before the policy sets freeze.</summary>
    public void Install(BlockEditPolicies blocks, ContainerAccessPolicies containers)
    {
        blocks.Register(BlockPolicy);
        containers.Register(ContainerPolicy);
    }

    private CommandBinding CreateCommand()
    {
        static Dictionary<string, object> Word(string word) => new(StringComparer.OrdinalIgnoreCase) { [word] = word };
        static IntegerCommandArgument Coordinate(string name) => new(name, -CoordinateLimit, CoordinateLimit);

        var definition = new CommandDefinition("protect", "Protect areas from edits and container use", ProtectNodes.Manage,
            new CommandOverload(
                new EnumCommandArgument("action", Word("create")), new StringCommandArgument("name"),
                Coordinate("x1"), Coordinate("y1"), Coordinate("z1"), Coordinate("x2"), Coordinate("y2"), Coordinate("z2")),
            new CommandOverload(new EnumCommandArgument("action", Word("remove")), new StringCommandArgument("name")),
            new CommandOverload(new EnumCommandArgument("action", Word("list"))));
        return new CommandBinding(definition, Execute);
    }

    private CommandFeedback Execute(in CommandInvocation invocation)
    {
        var arguments = invocation.Arguments;
        switch ((string)arguments["action"])
        {
            case "create":
                var region = ProtectRegion.Between(
                    (string)arguments["name"],
                    (int)arguments["x1"], (int)arguments["y1"], (int)arguments["z1"],
                    (int)arguments["x2"], (int)arguments["y2"], (int)arguments["z2"]);
                return Regions.TryAdd(region, out var error)
                    ? new("Protect", $"Protected {region.Name}.", CommandOutcome.Applied)
                    : new("Protect", error, CommandOutcome.Rejected);
            case "remove":
                var name = (string)arguments["name"];
                return Regions.TryRemove(name)
                    ? new("Protect", $"Removed {name}.", CommandOutcome.Applied)
                    : new("Protect", $"No region named '{name}'.", CommandOutcome.Rejected);
            default:
                var all = Regions.Snapshot;
                return new("Protect", all.IsEmpty ? "No protected regions." : string.Join(", ", all.Select(r => r.Name)), CommandOutcome.Applied);
        }
    }
}
