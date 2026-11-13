using System.Runtime.CompilerServices;

namespace Zenith.Logging;

public interface ILogger
{
    public void Clear() => Console.Clear();

    /// <summary>
    /// Cheap guard for a call site that would otherwise prepare a Debug-only value (LINQ, joins, ...)
    /// unconditionally: arguments are evaluated eagerly by the caller, so the level check inside Debug
    /// cannot prevent that cost. Plain <c>Debug($"...")</c> does not need it, see <see cref="DebugLogHandler"/>.
    /// Default <c>true</c> preserves existing behavior for any implementer that has no level filtering at all.
    /// </summary>
    public bool IsDebugEnabled => true;

    public void Debug(string message);

    /// <summary>
    /// Chosen by the compiler for <c>Debug($"...")</c>: with Debug off the message is never built and its arguments are not evaluated
    /// (see <see cref="DebugLogHandler"/>), so a Debug call site needs no <see cref="IsDebugEnabled"/> guard for plain interpolation.
    /// Only a call whose arguments are expensive to <i>prepare</i> outside the string (LINQ, joins) still needs the guard.
    /// </summary>
    public void Debug([InterpolatedStringHandlerArgument("")] ref DebugLogHandler message)
    {
        if (message.IsEnabled)
            Debug(message.ToStringAndClear());
    }

    public void Info(string message);

    public void Warning(string message);

    public void Error(string message);
}