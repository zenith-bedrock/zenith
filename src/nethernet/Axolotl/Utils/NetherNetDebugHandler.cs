using System.Runtime.CompilerServices;

namespace Zenith.NetherNet.Axolotl.Utils;

/// <summary>
/// Interpolated-string handler behind <see cref="INetherNetLogger.Debug(ref NetherNetDebugHandler)"/>: while Debug is off the message
/// is never formatted or allocated and the interpolated expressions are not evaluated; while on, the text equals a plain interpolated
/// string. Same design as <c>Zenith.Logging.DebugLogHandler</c>, kept separate because this package depends on no other Zenith package.
/// </summary>
[InterpolatedStringHandler]
public ref struct NetherNetDebugHandler
{
    private DefaultInterpolatedStringHandler _inner;

    public NetherNetDebugHandler(int literalLength, int formattedCount, INetherNetLogger logger, out bool shouldAppend)
    {
        IsEnabled = shouldAppend = logger.IsDebugEnabled;
        _inner = IsEnabled ? new DefaultInterpolatedStringHandler(literalLength, formattedCount) : default;
    }

    public bool IsEnabled { get; }

    public void AppendLiteral(string value) => _inner.AppendLiteral(value);

    public void AppendFormatted<T>(T value) => _inner.AppendFormatted(value);

    public void AppendFormatted<T>(T value, string? format) => _inner.AppendFormatted(value, format);

    public void AppendFormatted<T>(T value, int alignment) => _inner.AppendFormatted(value, alignment);

    public void AppendFormatted<T>(T value, int alignment, string? format) => _inner.AppendFormatted(value, alignment, format);

    public void AppendFormatted(ReadOnlySpan<char> value) => _inner.AppendFormatted(value);

    public void AppendFormatted(string? value) => _inner.AppendFormatted(value);

    public string ToStringAndClear() => _inner.ToStringAndClear();
}
