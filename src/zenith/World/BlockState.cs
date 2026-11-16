using System.Text;
using Zenith.Nbt;

namespace Zenith.World;

/// <summary>
/// The complete property set of one block state (e.g. <c>weirdo_direction=2, upside_down_bit=0</c>), keys sorted ordinally.
/// Values are normalised to strings: strings stay as-is, bytes/ints render as integers (so a bit is "0"/"1").
/// </summary>
sealed class BlockState
{
    public static readonly BlockState Empty = new(Array.Empty<KeyValuePair<string, string>>());

    private readonly KeyValuePair<string, string>[] _entries;

    private BlockState(KeyValuePair<string, string>[] sortedEntries) => _entries = sortedEntries;

    public int Count => _entries.Length;

    public IReadOnlyList<KeyValuePair<string, string>> Entries => _entries;

    public bool TryGet(string key, out string value)
    {
        foreach (var entry in _entries)
        {
            if (entry.Key == key)
            {
                value = entry.Value;
                return true;
            }
        }
        value = "";
        return false;
    }

    public string? Get(string key) => TryGet(key, out var value) ? value : null;

    public static BlockState FromNbt(NbtCompound states)
    {
        if (states.Count == 0) return Empty;
        var list = new List<KeyValuePair<string, string>>(states.Count);
        foreach (var (key, tag) in states.Entries)
        {
            if (tag.TryGetString(out var text)) list.Add(new(key, text));
            else if (tag.TryGetByte(out var b)) list.Add(new(key, b.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            else if (tag.TryGetInt(out var i)) list.Add(new(key, i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        return Create(list);
    }

    public static BlockState Create(IEnumerable<KeyValuePair<string, string>> entries)
    {
        var array = entries.ToArray();
        Array.Sort(array, static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        return array.Length == 0 ? Empty : new BlockState(array);
    }

    /// <summary>This state with the given properties replaced; null when an override names a property this state lacks.</summary>
    public BlockState? With(IReadOnlyDictionary<string, string> overrides)
    {
        if (overrides.Count == 0) return this;
        var copy = (KeyValuePair<string, string>[])_entries.Clone();
        foreach (var (key, value) in overrides)
        {
            var index = Array.FindIndex(copy, e => e.Key == key);
            if (index < 0) return null;
            copy[index] = new(key, value);
        }
        return new BlockState(copy);
    }

    public string CanonicalKey(string name)
    {
        var sb = new StringBuilder(name.Length + _entries.Length * 16);
        sb.Append(name);
        foreach (var (key, value) in _entries) sb.Append('\0').Append(key).Append('=').Append(value);
        return sb.ToString();
    }

    public override string ToString() => string.Join(',', _entries.Select(e => e.Key + "=" + e.Value));
}
