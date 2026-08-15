namespace Zenith.Raknet.Log;

public interface ILogger
{
    public void Clear() => Console.Clear();

    /// <summary>
    /// Cheap guard for a hot call site that would otherwise build a Debug-only message (string
    /// interpolation, LINQ, etc.) unconditionally — arguments to <see cref="Debug"/> are evaluated
    /// eagerly by the caller before the call, so the level check inside <see cref="Debug"/> alone
    /// cannot prevent that cost. Default <c>true</c> preserves existing behavior for any
    /// implementer that has no level filtering at all.
    /// </summary>
    public bool IsDebugEnabled => true;

    public void Debug(string message);

    public void Info(string message);

    public void Warning(string message);

    public void Error(string message);
}