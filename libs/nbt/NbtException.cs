namespace Zenith.Nbt;

public sealed class NbtException : Exception
{
    public NbtException(string message) : base(message) { }
    public NbtException(string message, Exception inner) : base(message, inner) { }
}
