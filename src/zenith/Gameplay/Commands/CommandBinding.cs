using Zenith.Gameplay.Permissions;

namespace Zenith.Gameplay.Commands;

/// <summary>
/// What a command line achieved, kept honest about when the world actually changed. <see cref="Accepted"/> is not
/// <see cref="Applied"/>: an accepted command queued work that the owner runs (and may still refuse) on a later tick.
/// </summary>
enum CommandOutcome
{
    /// <summary>Nothing happened: unknown, denied, malformed, or refused by the command itself.</summary>
    Rejected,

    /// <summary>Validly queued as an intent; the effect happens later on the owner and is not guaranteed.</summary>
    Accepted,

    /// <summary>Done before the command returned (a read-only answer, or a change already committed).</summary>
    Applied
}

/// <summary>What the sender is told, and how far the command got.</summary>
readonly record struct CommandFeedback(string Title, string Message, CommandOutcome Outcome);

/// <summary>A player named by value: enough to find them again on the owner, never the live player.</summary>
readonly record struct PlayerRef(Guid Id, string Name, long SessionId);

/// <summary>
/// One parsed command call, as values only: who is calling (principal and name), where they stand, and the parsed arguments
/// (player arguments arrive as <see cref="PlayerRef"/>). A handler never receives the live player.
/// </summary>
readonly record struct CommandInvocation(
    Principal Caller,
    string CallerName,
    float X,
    float Y,
    float Z,
    IReadOnlyDictionary<string, object> Arguments);

delegate CommandFeedback CommandHandler(in CommandInvocation invocation);

/// <summary>A command's grammar bound to the code that runs it, registered together before composition ends.</summary>
sealed record CommandBinding(CommandDefinition Definition, CommandHandler Handler);
