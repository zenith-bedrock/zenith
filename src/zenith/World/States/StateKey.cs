using System.Runtime.CompilerServices;

namespace Zenith.World;

/// <summary>
/// A block-state property known to the server, declared once in <see cref="StateKeys"/>. A key carries its wire name and
/// the wire spelling of each value, so no code outside that one file ever writes a property name or value as a string.
/// Keys get a dense <see cref="Slot"/> so a block family finds "its" property by array index.
/// </summary>
abstract class StateKey
{
    private static readonly List<StateKey> All = [];

    protected StateKey(string name)
    {
        Name = name;
        lock (All)
        {
            Slot = All.Count;
            All.Add(this);
        }
    }

    public string Name { get; }

    /// <summary>Dense index in registration order.</summary>
    public int Slot { get; }

    /// <summary>Number of values the key can express (0 for presence-only keys).</summary>
    internal abstract int WireCount { get; }

    internal abstract string WireName(int index);

    internal static int RegisteredCount
    {
        get { EnsureDeclared(); lock (All) return All.Count; }
    }

    internal static StateKey? Find(string name)
    {
        EnsureDeclared();
        lock (All)
        {
            foreach (var key in All)
                if (key.Name == name) return key;
        }
        return null;
    }

    private static void EnsureDeclared() => RuntimeHelpers.RunClassConstructor(typeof(StateKeys).TypeHandle);
}

/// <summary>A 0/1 property (<c>open_bit</c>, <c>upper_block_bit</c>…).</summary>
sealed class BitKey(string name) : StateKey(name)
{
    internal override int WireCount => 2;

    internal override string WireName(int index) => index == 0 ? "0" : "1";
}

/// <summary>A property the server only needs to recognise (classification), never to read or write.</summary>
sealed class PresenceKey(string name) : StateKey(name)
{
    internal override int WireCount => 0;

    internal override string WireName(int index) => throw new InvalidOperationException("Presence keys have no values.");
}

/// <summary>
/// A property whose values map to a byte-backed enum. <paramref name="wire"/> lists the wire spelling of each enum member
/// in declaration order (member 0 first), which is the only place that correspondence is written down.
/// </summary>
sealed class EnumKey<T> : StateKey where T : unmanaged, Enum
{
    private readonly string[] _wire;

    public EnumKey(string name, params string[] wire) : base(name)
    {
        if (Unsafe.SizeOf<T>() != 1) throw new InvalidOperationException($"State enum {typeof(T).Name} must be byte-backed.");
        _wire = wire;
    }

    internal override int WireCount => _wire.Length;

    internal override string WireName(int index) => _wire[index];

    internal static int ToIndex(T value) => Unsafe.As<T, byte>(ref value);

    internal static T FromIndex(int index)
    {
        var b = (byte)index;
        return Unsafe.As<byte, T>(ref b);
    }
}
