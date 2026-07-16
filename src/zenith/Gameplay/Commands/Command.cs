using Zenith.Protocol;

namespace Zenith.Gameplay.Commands;

class Command
{
    public string Name { get; }
    public string Description { get; }
    public string[] Aliases { get; }
    public CommandPermissionLevel PermissionLevel { get; }
    public Action<CommandContext> Execute { get; }

    public Command(
        string name,
        string description,
        Action<CommandContext> execute,
        CommandPermissionLevel permissionLevel = CommandPermissionLevel.Any,
        string[]? aliases = null)
    {
        Name = name;
        Description = description;
        Execute = execute;
        PermissionLevel = permissionLevel;
        Aliases = aliases ?? [];
    }
}
