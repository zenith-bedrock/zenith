using System.Collections.Frozen;

namespace Zenith.World;

/// <summary>Where a runtime id lives: its family and its ordinal inside the family's table.</summary>
readonly record struct StateInfo(BlockFamily Family, int Ordinal);

/// <summary>
/// Typed, allocation-free access to block-state properties by runtime id. Built once from the palette; every read and
/// edit is a dictionary hit plus integer arithmetic. Shapes use it instead of comparing strings.
/// </summary>
sealed class BlockStateTable
{
    private readonly FrozenDictionary<int, StateInfo> _info;
    private readonly FrozenDictionary<string, BlockFamily> _families;

    private BlockStateTable(FrozenDictionary<int, StateInfo> info, FrozenDictionary<string, BlockFamily> families)
    {
        _info = info;
        _families = families;
    }

    public IEnumerable<BlockFamily> Families => _families.Values;

    public BlockFamily? FamilyOf(int runtimeId) => _info.TryGetValue(runtimeId, out var info) ? info.Family : null;

    public bool TryGetFamily(string name, out BlockFamily family) => _families.TryGetValue(name, out family!);

    /// <summary>The bit's value; false when the block has no such property.</summary>
    public bool Bit(int runtimeId, BitKey key) => TryGet(runtimeId, key, out var value) && value;

    public bool TryGet(int runtimeId, BitKey key, out bool value)
    {
        value = false;
        if (!TryLocate(runtimeId, key, out var property, out var local)) return false;
        var wire = property.LocalToWire[local];
        value = wire == 1;
        return wire >= 0;
    }

    public bool TryGet<T>(int runtimeId, EnumKey<T> key, out T value) where T : unmanaged, Enum
    {
        value = default;
        if (!TryLocate(runtimeId, key, out var property, out var local)) return false;
        var wire = property.LocalToWire[local];
        if (wire < 0) return false;
        value = EnumKey<T>.FromIndex(wire);
        return true;
    }

    /// <summary>Replaces the property in <paramref name="runtimeId"/>; false (id untouched) when the block lacks it or the state does not exist.</summary>
    public bool TrySet(ref int runtimeId, BitKey key, bool value) => TrySetWire(ref runtimeId, key, value ? 1 : 0);

    public bool TrySet<T>(ref int runtimeId, EnumKey<T> key, T value) where T : unmanaged, Enum =>
        TrySetWire(ref runtimeId, key, EnumKey<T>.ToIndex(value));

    private bool TrySetWire(ref int runtimeId, StateKey key, int wire)
    {
        if (!_info.TryGetValue(runtimeId, out var info)) return false;
        var family = info.Family;
        var index = family.PropertyIndex(key);
        if (index < 0) return false;
        var property = family.Properties[index];
        if (property.Stride == 0 || (uint)wire >= (uint)property.WireToLocal.Length || property.WireToLocal[wire] < 0) return false;

        var current = info.Ordinal / property.Stride % property.Values.Length;
        var ordinal = info.Ordinal + (property.WireToLocal[wire] - current) * property.Stride;
        if (!family.TryGetId(ordinal, out var id)) return false;
        runtimeId = id;
        return true;
    }

    private bool TryLocate(int runtimeId, StateKey key, out StateProperty property, out int local)
    {
        property = null!;
        local = 0;
        if (!_info.TryGetValue(runtimeId, out var info)) return false;
        var index = info.Family.PropertyIndex(key);
        if (index < 0) return false;
        property = info.Family.Properties[index];
        if (property.Stride == 0) return false;
        local = info.Ordinal / property.Stride % property.Values.Length;
        return true;
    }

    internal static BlockStateTable Build(BlockPalette palette)
    {
        var grouped = new Dictionary<string, List<(int Id, BlockState State)>>(StringComparer.Ordinal);
        foreach (var (id, state) in palette.AllStates)
        {
            if (!palette.TryGetName(id, out var name)) continue;
            if (!grouped.TryGetValue(name, out var list)) grouped[name] = list = [];
            list.Add((id, state));
        }

        var families = new Dictionary<string, BlockFamily>(grouped.Count, StringComparer.Ordinal);
        var info = new Dictionary<int, StateInfo>(palette.StateCount);
        foreach (var (name, states) in grouped)
        {
            var family = BlockFamily.Create(name, palette.TryGet(name, out var def) ? def : states[0].Id, states, out var ordinals);
            families[name] = family;
            for (var i = 0; i < states.Count; i++) info[states[i].Id] = new StateInfo(family, ordinals[i]);
        }

        return new BlockStateTable(info.ToFrozenDictionary(), families.ToFrozenDictionary(StringComparer.Ordinal));
    }
}
