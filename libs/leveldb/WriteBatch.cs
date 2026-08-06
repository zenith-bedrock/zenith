using System.Buffers.Binary;

namespace Zenith.LevelDB;

/// <summary>
/// Agrupa Puts/Deletes aplicados atomicamente via <see cref="DB.Write"/>.
/// Encoding: header LE seq:u64 + count:u32, depois ops
/// type:u8 + keyLen:u32 + key [+ valLen:u32 + value se Put].
/// </summary>
public sealed class WriteBatch
{
    public const byte OpPut = 1;
    public const byte OpDelete = 2;

    private readonly MemoryStream _ops = new();
    private uint _count;

    public int Count => (int)_count;

    public void Put(byte[] key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        _ops.WriteByte(OpPut);
        KvFraming.WriteLengthPrefixed(_ops, key);
        KvFraming.WriteLengthPrefixed(_ops, value);
        _count++;
    }

    public void Delete(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _ops.WriteByte(OpDelete);
        KvFraming.WriteLengthPrefixed(_ops, key);
        _count++;
    }

    public void Clear()
    {
        _ops.SetLength(0);
        _ops.Position = 0;
        _count = 0;
    }

    /// <summary>Alias de <see cref="Clear"/> (API LevelDB clássica).</summary>
    public void Reset() => Clear();

    /// <summary>Blob completo para o journal: header + ops.</summary>
    internal byte[] Encode(ulong seq = 0)
    {
        var ops = _ops.ToArray();
        var blob = new byte[12 + ops.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(blob.AsSpan(0, 8), seq);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(8, 4), _count);
        ops.CopyTo(blob.AsSpan(12));
        return blob;
    }

    internal static void ApplyEncoded(ReadOnlySpan<byte> encoded, MemTable mem)
    {
        if (encoded.Length < 12)
            throw new InvalidDataException("leveldb: batch record too short");

        var count = BinaryPrimitives.ReadUInt32LittleEndian(encoded.Slice(8, 4));
        ApplyPayload(encoded.Slice(12), count, mem);
    }

    private static void ApplyPayload(ReadOnlySpan<byte> ops, uint expectedCount, MemTable mem)
    {
        var pos = 0;
        uint n = 0;
        while (pos < ops.Length)
        {
            var type = ops[pos++];

            if (!KvFraming.TryReadLengthPrefixed(ops, ref pos, out var key))
                throw new InvalidDataException("leveldb: corrupted batch key");

            if (type == OpPut)
            {
                if (!KvFraming.TryReadLengthPrefixed(ops, ref pos, out var value))
                    throw new InvalidDataException("leveldb: corrupted batch value");
                mem.Put(key.ToArray(), value.ToArray());
            }
            else if (type == OpDelete)
            {
                mem.Delete(key.ToArray());
            }
            else
            {
                throw new InvalidDataException($"leveldb: unknown batch op type {type}");
            }

            n++;
        }

        if (n != expectedCount)
            throw new InvalidDataException("leveldb: batch count mismatch");
    }
}
