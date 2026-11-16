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

    public bool ContainsKey(string name) => _index.ContainsKey(name);

    /// <summary>Removes <paramref name="name"/>; later entries keep their order.</summary>
    public bool Remove(string name)
    {
        if (!_index.Remove(name, out var at)) return false;
        _entries.RemoveAt(at);
        for (var i = at; i < _entries.Count; i++)
            _index[_entries[i].Key] = i;
        return true;
    }

    // Fluent setters, so building a compound reads as one expression.
    public NbtCompound SetByte(string name, sbyte value) => Put(name, NbtTag.Byte(value));
    public NbtCompound SetBool(string name, bool value) => Put(name, NbtTag.Byte((sbyte)(value ? 1 : 0)));
    public NbtCompound SetShort(string name, short value) => Put(name, NbtTag.Short(value));
    public NbtCompound SetInt(string name, int value) => Put(name, NbtTag.Int(value));
    public NbtCompound SetLong(string name, long value) => Put(name, NbtTag.Long(value));
    public NbtCompound SetFloat(string name, float value) => Put(name, NbtTag.Float(value));
    public NbtCompound SetDouble(string name, double value) => Put(name, NbtTag.Double(value));
    public NbtCompound SetString(string name, string value) => Put(name, NbtTag.String(value));
    public NbtCompound SetCompound(string name, NbtCompound value) => Put(name, NbtTag.Compound(value));
    public NbtCompound SetList(string name, NbtList value) => Put(name, NbtTag.List(value));

    // Typed reads: false when the entry is missing or has another type.
    public bool TryGetInt(string name, out int value)
    {
        value = 0;
        return TryGet(name, out var tag) && tag.TryGetInt(out value);
    }

    public bool TryGetString(string name, out string value)
    {
        value = "";
        return TryGet(name, out var tag) && tag.TryGetString(out value);
    }

    public bool TryGetCompound(string name, out NbtCompound value)
    {
        value = null!;
        return TryGet(name, out var tag) && tag.TryGetCompound(out value);
    }

    public bool TryGetList(string name, out NbtList value)
    {
        value = null!;
        return TryGet(name, out var tag) && tag.TryGetList(out value);
    }

    private NbtCompound Put(string name, NbtTag tag)
    {
        Set(name, tag);
        return this;
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
