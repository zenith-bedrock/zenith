namespace Zenith.Nbt;

/// <summary>Ordered compound map (insertion order preserved for stable encode).</summary>
public sealed class NbtCompound
{
    private readonly List<KeyValuePair<string, NbtTag>> _entries = new();
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    public int Count => _entries.Count;

    public IReadOnlyList<KeyValuePair<string, NbtTag>> Entries => _entries;

    public void Set(string name, NbtTag value)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_index.TryGetValue(name, out var i))
        {
            _entries[i] = new KeyValuePair<string, NbtTag>(name, value);
            return;
        }

        _index[name] = _entries.Count;
        _entries.Add(new KeyValuePair<string, NbtTag>(name, value));
    }

    public bool TryGet(string name, out NbtTag tag)
    {
        if (_index.TryGetValue(name, out var i))
        {
            tag = _entries[i].Value;
            return true;
        }

        tag = default;
        return false;
    }

    public NbtTag this[string name]
    {
        get
        {
            if (!TryGet(name, out var tag))
                throw new NbtException($"Compound has no entry named '{name}'.");
            return tag;
        }
    }
}

/// <summary>Homogeneous list of NBT payloads (element type on the wire).</summary>
public sealed class NbtList
{
    private readonly List<NbtTag> _items = new();

    public NbtList(NbtType elementType) => ElementType = elementType;

    public NbtType ElementType { get; }

    public int Count => _items.Count;

    public IReadOnlyList<NbtTag> Items => _items;

    public void Add(NbtTag tag)
    {
        if (tag.Type != ElementType)
            throw new NbtException($"List element type is {ElementType}, got {tag.Type}.");
        _items.Add(tag);
    }

    public NbtTag this[int index] => _items[index];
}
