using Zenith.Nbt;

namespace Zenith.World;

/// <summary>
/// The per-stack state an item carries besides what it is (ADR §148): what BDS keeps in the stack's user-data tag.
/// Wear is modeled as <see cref="Damage"/>; other tags are retained as immutable metadata (ADR §156).
/// Retaining an enchantment's tag does not implement its combat effect. It is part of <see cref="StackId"/> because
/// two stacks that differ in it are not the same stack (they never merge), exactly as for aux — but it is a value of its
/// own, never folded into aux: aux is a variant (bed colour), user data is state that changes with use.
/// </summary>
readonly record struct ItemUserData
{
    /// <summary>Largest damage a stack can carry (generous over the real maximum, 2031 for netherite tools).</summary>
    public const int MaxDamage = short.MaxValue;

    public int Damage { get; }

    /// <summary>Immutable LE NBT compound, excluding the separately modeled Damage field.</summary>
    public string? TagBase64 { get; }

    public ItemUserData(int damage, string? tagBase64 = null)
    {
        TagBase64 = null;
        if (tagBase64 is not null)
        {
            var bytes = Convert.FromBase64String(tagBase64);
            var decoded = NbtCodec.Decode(bytes, NbtEncoding.LittleEndian);
            if (decoded.BytesConsumed != bytes.Length || !decoded.Root.Tag.TryGetCompound(out var compound))
                throw new InvalidDataException("Item user data must be one complete NBT compound.");
            var retained = new NbtCompound();
            foreach (var entry in compound.Entries.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                if (entry.Key == "Damage")
                {
                    if (!entry.Value.TryGetInt(out var wear))
                        throw new InvalidDataException("Item Damage must be an integer.");
                    if (damage == 0) damage = wear;
                }
                else retained.Set(entry.Key, entry.Value);
            }
            if (retained.Count > 0)
                TagBase64 = Convert.ToBase64String(NbtCodec.Encode(
                    new NbtNamedTag("", NbtTag.Compound(retained)), NbtEncoding.LittleEndian));
        }
        if (damage < 0 || damage > MaxDamage)
            throw new ArgumentOutOfRangeException(nameof(damage), damage, $"damage must be 0..{MaxDamage}");
        Damage = damage;
    }

    /// <summary>A stack with no user data: what every freshly made item has.</summary>
    public bool IsDefault => Damage == 0 && TagBase64 is null;
}
