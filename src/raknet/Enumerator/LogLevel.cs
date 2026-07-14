namespace Zenith.Raknet.Enumerator;

[Flags]
public enum LogLevel
{
    None = 0,
    Info = 1 << 0,
    Warning = 1 << 1,
    Error = 1 << 2,
    Debug = 1 << 3,
    All = Info | Warning | Error | Debug
}