using System.Runtime.CompilerServices;

namespace Zenith.Logging;

/// <summary>
/// Interpolated-string handler behind <see cref="ILogger.Debug(ref DebugLogHandler)"/>. The compiler builds
/// <c>logger.Debug($"... {x} ...")</c> through it: when Debug is off, <c>shouldAppend</c> is false, so the message is never formatted,
/// no string is allocated and the interpolated expressions are not even evaluated. When Debug is on it formats exactly like a plain
/// interpolated string.
/// </summary>
[InterpolatedStringHandler]
public ref struct DebugLogHandler
{
    private DefaultInterpolatedStringHandler _inner;

    public DebugLogHandler(int literalLength, int formattedCount, ILogger logger, out bool shouldAppend)
    {
        IsEnabled = shouldAppend = logger.IsDebugEnabled;
        _inner = IsEnabled ? new DefaultInterpolatedStringHandler(literalLength, formattedCount) : default;
    }

    /// <summary>False when the logger had Debug off: the handler holds nothing and there is nothing to send.</summary>
    public bool IsEnabled { get; }

    public void AppendLiteral(string value) => _inner.AppendLiteral(value);

    public void AppendFormatted<T>(T value) => _inner.AppendFormatted(value);

    public void AppendFormatted<T>(T value, string? format) => _inner.AppendFormatted(value, format);

    public void AppendFormatted<T>(T value, int alignment) => _inner.AppendFormatted(value, alignment);

    public void AppendFormatted<T>(T value, int alignment, string? format) => _inner.AppendFormatted(value, alignment, format);

    public void AppendFormatted(ReadOnlySpan<char> value) => _inner.AppendFormatted(value);

    public void AppendFormatted(string? value) => _inner.AppendFormatted(value);

    /// <summary>The formatted message; releases the pooled buffer. Call once, only when <see cref="IsEnabled"/>.</summary>
    public string ToStringAndClear() => _inner.ToStringAndClear();
}
