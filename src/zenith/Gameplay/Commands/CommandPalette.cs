namespace Zenith.Gameplay.Commands;

class CommandPalette
{
    private readonly Dictionary<string, Command> _commands = new();

    public void Register(Command command)
    {
        _commands[command.Name] = command;
        foreach (var alias in command.Aliases)
            _commands.TryAdd(alias, command);
    }

    public Command? Get(string name)
    {
        if (name.StartsWith('/'))
            name = name[1..];
        var first = name.Split(' ')[0];
        return _commands.GetValueOrDefault(first);
    }

    public IReadOnlyCollection<Command> GetAll()
    {
        var unique = new HashSet<Command>(_commands.Values);
        return unique;
    }
}
