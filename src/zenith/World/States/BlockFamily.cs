namespace Zenith.World;

/// <summary>Facts about a block type that never depend on its state, decided once when the catalog is built.</summary>
[Flags]
enum BlockTraits : byte
{
    None = 0,

    /// <summary>Plants may grow on it (grass, dirt, farmland…).</summary>
    Soil = 1,

    /// <summary>Plant that only survives on soil (mushrooms, fungi and roots accept any solid ground).</summary>
    NeedsSoil = 2,

    /// <summary>Opens and closes by redstone only (iron door / trapdoor).</summary>
    RedstoneOnly = 4,

    /// <summary>A wall, fence or pane visually joins this full block.</summary>
    WallConnectable = 8
}

/// <summary>One property of a block type: its legal values and the mixed-radix stride of the state ordinal.</summary>
sealed class StateProperty
{
    internal StateProperty(string name, string[] values, int stride, StateKey? key)
    {
        Name = name;
        Values = values;
        Stride = stride;
        Key = key;
        if (key is null) return;

        // Translate between the key's wire value index and this block's local value index, once.
        WireToLocal = new sbyte[key.WireCount];
        LocalToWire = new sbyte[values.Length];
        Array.Fill(WireToLocal, (sbyte)-1);
        Array.Fill(LocalToWire, (sbyte)-1);
        for (var wire = 0; wire < WireToLocal.Length; wire++)
        {
            var local = Array.IndexOf(values, key.WireName(wire));
            if (local < 0) continue;
            WireToLocal[wire] = (sbyte)local;
            LocalToWire[local] = (sbyte)wire;
        }
    }

    public string Name { get; }

    public string[] Values { get; }

    /// <summary>Distance between two consecutive values of this property in the ordinal; 0 when the family is too large to tabulate.</summary>
    public int Stride { get; }

    public StateKey? Key { get; }

    internal sbyte[] WireToLocal { get; } = [];

    internal sbyte[] LocalToWire { get; } = [];
}

/// <summary>
/// All states of one block type (<c>minecraft:oak_stairs</c>) laid out as a mixed-radix table: the runtime id of any
/// state is found from another state of the family by arithmetic on its ordinal, so changing a property costs no
/// allocation and no string work.
/// </summary>
sealed class BlockFamily
{
    private const int MaxTableSize = 1 << 16;

    private readonly int[] _propBySlot;
    private readonly int[]? _ids;
    private readonly bool[]? _present;

    private BlockFamily(string name, int defaultId, StateProperty[] properties, int[]? ids, bool[]? present)
    {
        Name = name;
        DefaultId = defaultId;
        Properties = properties;
        _ids = ids;
        _present = present;
        _propBySlot = new int[StateKey.RegisteredCount];
        Array.Fill(_propBySlot, -1);
        for (var i = 0; i < properties.Length; i++)
            if (properties[i].Key is { } key) _propBySlot[key.Slot] = i;
    }

    public string Name { get; }

    public int DefaultId { get; }

    public StateProperty[] Properties { get; }

    public BlockKind Kind { get; internal set; }

    public BlockTraits Traits { get; internal set; }

    public DigProfile? Dig { get; internal set; }

    /// <summary>For a slab: the double-slab family it merges into.</summary>
    public BlockFamily? DoubleSlab { get; internal set; }

    public bool Has(StateKey key) => key.Slot < _propBySlot.Length && _propBySlot[key.Slot] >= 0;

    internal int PropertyIndex(StateKey key) => key.Slot < _propBySlot.Length ? _propBySlot[key.Slot] : -1;

    /// <summary>Runtime id at <paramref name="ordinal"/>, or false when the table has no such state.</summary>
    internal bool TryGetId(int ordinal, out int id)
    {
        id = 0;
        if (_ids is null || (uint)ordinal >= (uint)_ids.Length || _present is { } present && !present[ordinal]) return false;
        id = _ids[ordinal];
        return true;
    }

    /// <summary>
    /// Builds the family from its states. Families that are irregular or too large to tabulate still classify, but
    /// their states cannot be edited by key.
    /// </summary>
    internal static BlockFamily Create(string name, int defaultId, List<(int Id, BlockState State)> states, out int[] ordinals)
    {
        var first = states[0].State.Entries;
        var count = first.Count;
        var values = new List<string>[count];
        for (var i = 0; i < count; i++) values[i] = [];

        var regular = true;
        foreach (var (_, state) in states)
        {
            if (state.Count != count) { regular = false; continue; }
            for (var i = 0; i < count; i++)
            {
                var entry = state.Entries[i];
                if (entry.Key != first[i].Key) { regular = false; continue; }
                if (!values[i].Contains(entry.Value)) values[i].Add(entry.Value);
            }
        }

        var properties = new StateProperty[count];
        long size = 1;
        for (var i = 0; i < count; i++)
        {
            var stride = size > MaxTableSize ? 0 : (int)size;
            properties[i] = new StateProperty(first[i].Key, [.. values[i]], stride, StateKey.Find(first[i].Key));
            size *= Math.Max(1, values[i].Count);
        }

        ordinals = new int[states.Count];
        if (!regular || size > MaxTableSize)
            return new BlockFamily(name, defaultId, properties, null, null);

        var ids = new int[size];
        var present = new bool[size];
        for (var s = 0; s < states.Count; s++)
        {
            var ordinal = 0;
            for (var i = 0; i < count; i++)
                ordinal += values[i].IndexOf(states[s].State.Entries[i].Value) * properties[i].Stride;
            ordinals[s] = ordinal;
            ids[ordinal] = states[s].Id;
            present[ordinal] = true;
        }

        return new BlockFamily(name, defaultId, properties, ids, size == states.Count ? null : present);
    }
}
