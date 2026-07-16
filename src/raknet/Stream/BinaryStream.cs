using System.Buffers.Binary;
using System.Text;

namespace Zenith.Raknet.Stream;

/// <summary>
/// ref struct de propósito duplo: leitura sobre um array existente (dispatch de pacote) e
/// escrita crescendo por dobragem (encode). Por ser ref struct, <c>new BinaryStream(buffer)</c>
/// não aloca nenhum objeto no heap - só um valor na stack - o que importa porque isso acontece
/// uma vez por pacote recebido. Efeitos colaterais dessa escolha (documentados nos pontos que
/// importam): não pode virar campo de classe, não pode ser capturado em lambda/async, e todo
/// método que precisa que a posição de leitura avançada seja visível pra quem chamou (ex:
/// header primeiro, payload depois) precisa receber por <c>ref</c> - do contrário cada chamada
/// mexe só na própria cópia local e quem chamou nunca vê o avanço.
/// </summary>
public ref struct BinaryStream : IDisposable
{
    public enum Endianess : byte
    {
        Big,
        Little
    }

    private const int MinGrowth = 64;
    private bool _disposed;

    public int Offset { get; set; }

    /// <summary>
    /// Backing array. Em modo de escrita pode estar sobre-alocado (capacidade > dados
    /// válidos); use <see cref="Length"/> pra saber quantos bytes são válidos, nunca
    /// <c>Buffer.Length</c>. Em leitura com <see cref="BinaryStream(byte[])"/>,
    /// <see cref="Length"/> == <c>Buffer.Length</c>; com
    /// <see cref="BinaryStream(byte[], int)"/>, <see cref="Length"/> é o prefixo válido
    /// (ex. buffer do <c>ArrayPool</c>).
    /// </summary>
    public byte[] Buffer { get; private set; }

    /// <summary>Quantidade de bytes válidos no buffer. Distinto de <c>Buffer.Length</c> quando em modo de escrita.</summary>
    public int Length { get; private set; }

    public bool IsEndOfFile => Offset >= Length;

    /// <summary>
    /// Finaliza um buffer de escrita e devolve exatamente os bytes válidos (sem cópia extra:
    /// é só um slice sobre o array já alocado). Chame só quando terminar de escrever.
    /// </summary>
    public Span<byte> GetBufferDisposing()
    {
        ThrowIfDisposed();
        var span = new Span<byte>(Buffer, 0, Length);
        Buffer = Array.Empty<byte>();
        Length = 0;
        Offset = 0;
        _disposed = true;
        return span;
    }

    /// <summary>Modo de escrita: começa vazio e cresce conforme Write/WriteX são chamados.</summary>
    public BinaryStream()
    {
        Buffer = Array.Empty<byte>();
        Length = 0;
        Offset = 0;
    }

    /// <summary>Modo de leitura: envelopa um array já existente, sem copiar.</summary>
    public BinaryStream(byte[] buffer)
    {
        Buffer = buffer;
        Length = buffer.Length;
        Offset = 0;
    }

    /// <summary>
    /// Modo de leitura: envelopa um array já existente com comprimento explícito.
    /// Útil para buffers do <see cref="System.Buffers.ArrayPool{T}"/> cujo tamanho real
    /// (<paramref name="length"/>) pode ser menor que <c>buffer.Length</c>.
    /// </summary>
    public BinaryStream(byte[] buffer, int length)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if ((uint)length > (uint)buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(length), length, "Length must be within the buffer.");

        Buffer = buffer;
        Length = length;
        Offset = 0;
    }

    public void Rewind()
    {
        ThrowIfDisposed();
        Offset = 0;
    }

    public Span<byte> ReadSpan(int len)
    {
        ThrowIfDisposed();

        switch (len)
        {
            case < 0:
                throw new ArgumentException("Length must be positive");
            case 0:
                return Span<byte>.Empty;
        }
        var remaining = Length - Offset;
        if (remaining < len)
        {
            throw new InvalidOperationException($"Not enough bytes left in buffer: need {len}, have {remaining}");
        }

        var result = new Span<byte>(Buffer, Offset, len);
        Offset += len;
        return result;
    }

    public Span<byte> ReadRemaining()
    {
        ThrowIfDisposed();
        if (Offset >= Length)
        {
            throw new InvalidOperationException("No bytes left to read");
        }

        var remainingSpan = new Span<byte>(Buffer, Offset, Length - Offset);
        Offset = Length;
        return remainingSpan;
    }

    /// <summary>
    /// Garante espaço pra mais <paramref name="additional"/> bytes, crescendo a capacidade
    /// por dobragem (igual List&lt;T&gt;) em vez de realocar exatamente o necessário toda
    /// vez. Isso troca um realloc+copy por Write (O(n²) no total) por um número logarítmico
    /// de reallocs (O(n) amortizado).
    /// </summary>
    private void EnsureCapacity(int additional)
    {
        ThrowIfDisposed();
        var required = Length + additional;
        if (required <= Buffer.Length) return;

        var newCapacity = Math.Max(Buffer.Length * 2, MinGrowth);
        if (newCapacity < required) newCapacity = required;

        var newBuffer = new byte[newCapacity];
        System.Buffer.BlockCopy(Buffer, 0, newBuffer, 0, Length);
        Buffer = newBuffer;
    }

    public void Write(scoped ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();
        if (data.Length == 0) return;

        EnsureCapacity(data.Length);
        data.CopyTo(Buffer.AsSpan(Length));
        Length += data.Length;
    }

    public bool ReadBool() => ReadSpan(1)[0] != 0;

    public void WriteBool(bool v) => WriteByte(v ? (byte)1 : (byte)0);

    public byte ReadByte() => ReadSpan(1)[0];

    /// <summary>Escreve um único byte sem alocar nenhum array temporário.</summary>
    public void WriteByte(byte v)
    {
        ThrowIfDisposed();
        EnsureCapacity(1);
        Buffer[Length] = v;
        Length += 1;
    }

    public void WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteUShort((ushort)bytes.Length);
        Write(bytes);
    }

    public string ReadString()
    {
        var length = ReadUShort();
        var bytes = ReadSpan(length);
        return Encoding.UTF8.GetString(bytes);
    }

    public void WriteVarString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteUnsignedVarInt(bytes.Length);
        Write(bytes);
    }

    public string ReadVarString()
    {
        var length = ReadUnsignedVarInt();
        var bytes = ReadSpan(length);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Blob Bedrock length-prefixed (unsigned varint + bytes). Diferente de
    /// <see cref="WriteVarString"/>: não passa por UTF-8.
    /// </summary>
    public void WriteByteArray(scoped ReadOnlySpan<byte> data)
    {
        WriteUnsignedVarInt(data.Length);
        Write(data);
    }

    /// <summary>
    /// Lê byte array prefixado com unsigned varint (contraparte de <see cref="WriteByteArray"/>).
    /// </summary>
    public byte[] ReadByteArray()
    {
        var length = ReadUnsignedVarInt();
        return ReadSpan(length).ToArray();
    }

    /// <summary>
    /// UUID Bedrock: bytes RFC 4122 com cada metade de 8 bytes invertida no wire (espelha <see cref="WriteUuid"/>).
    /// </summary>
    public Guid ReadUuid()
    {
        Span<byte> wire = stackalloc byte[16];
        ReadSpan(16).CopyTo(wire);
        wire[..8].Reverse();
        wire[8..].Reverse();

        Span<byte> le = stackalloc byte[16];
        le[0] = wire[3];
        le[1] = wire[2];
        le[2] = wire[1];
        le[3] = wire[0];
        le[4] = wire[5];
        le[5] = wire[4];
        le[6] = wire[7];
        le[7] = wire[6];
        wire[8..].CopyTo(le[8..]);

        return new Guid(le);
    }

    /// <summary>
    /// UUID Bedrock: bytes RFC 4122 com cada metade de 8 bytes invertida no wire.
    /// </summary>
    public void WriteUuid(Guid uuid)
    {
        Span<byte> rfc = stackalloc byte[16];
        Span<byte> mixed = stackalloc byte[16];
        uuid.TryWriteBytes(mixed);

        rfc[0] = mixed[3];
        rfc[1] = mixed[2];
        rfc[2] = mixed[1];
        rfc[3] = mixed[0];
        rfc[4] = mixed[5];
        rfc[5] = mixed[4];
        rfc[6] = mixed[7];
        rfc[7] = mixed[6];
        mixed[8..].CopyTo(rfc[8..]);

        rfc[..8].Reverse();
        rfc[8..].Reverse();
        Write(rfc);
    }

    public short ReadShort(Endianess end = Endianess.Big) =>
        end == Endianess.Little ? BinaryPrimitives.ReadInt16LittleEndian(ReadSpan(2)) :
        BinaryPrimitives.ReadInt16BigEndian(ReadSpan(2));

    public void WriteShort(short v, Endianess end = Endianess.Big)
    {
        Span<byte> buffer = stackalloc byte[2];
        if (end == Endianess.Little) BinaryPrimitives.WriteInt16LittleEndian(buffer, v);
        else BinaryPrimitives.WriteInt16BigEndian(buffer, v);
        Write(buffer);
    }

    public ushort ReadUShort(Endianess end = Endianess.Big) =>
        end == Endianess.Little ? BinaryPrimitives.ReadUInt16LittleEndian(ReadSpan(2)) :
        BinaryPrimitives.ReadUInt16BigEndian(ReadSpan(2));

    public void WriteUShort(ushort v, Endianess end = Endianess.Big)
    {
        Span<byte> buffer = stackalloc byte[2];
        if (end == Endianess.Little) BinaryPrimitives.WriteUInt16LittleEndian(buffer, v);
        else BinaryPrimitives.WriteUInt16BigEndian(buffer, v);
        Write(buffer);
    }

    public uint ReadTriad(Endianess end = Endianess.Big)
    {
        var value = 0;
        if (end == Endianess.Little)
        {
            value |= ReadByte();
            value |= ReadByte() << 8;
            value |= ReadByte() << 16;
        }
        else
        {
            value |= ReadByte() << 16;
            value |= ReadByte() << 8;
            value |= ReadByte();
        }
        return (uint)value;
    }

    public void WriteTriad(uint v, Endianess end = Endianess.Big)
    {
        if (end == Endianess.Little)
        {
            WriteByte((byte)(v & 0xff));
            WriteByte((byte)((v >> 8) & 0xff));
            WriteByte((byte)((v >> 16) & 0xff));
        }
        else
        {
            WriteByte((byte)((v >> 16) & 0xff));
            WriteByte((byte)((v >> 8) & 0xff));
            WriteByte((byte)(v & 0xff));
        }
    }

    public int ReadInt(Endianess end = Endianess.Big) =>
        end == Endianess.Little ? BinaryPrimitives.ReadInt32LittleEndian(ReadSpan(4)) :
        BinaryPrimitives.ReadInt32BigEndian(ReadSpan(4));

    public void WriteInt(int v, Endianess end = Endianess.Big)
    {
        Span<byte> buffer = stackalloc byte[4];
        if (end == Endianess.Little) BinaryPrimitives.WriteInt32LittleEndian(buffer, v);
        else BinaryPrimitives.WriteInt32BigEndian(buffer, v);
        Write(buffer);
    }

    public uint ReadUInt(Endianess end = Endianess.Big) =>
        end == Endianess.Little ? BinaryPrimitives.ReadUInt32LittleEndian(ReadSpan(4)) :
        BinaryPrimitives.ReadUInt32BigEndian(ReadSpan(4));

    public void WriteUInt(uint v, Endianess end = Endianess.Big)
    {
        Span<byte> buffer = stackalloc byte[4];
        if (end == Endianess.Little) BinaryPrimitives.WriteUInt32LittleEndian(buffer, v);
        else BinaryPrimitives.WriteUInt32BigEndian(buffer, v);
        Write(buffer);
    }

    public float ReadFloat(Endianess end = Endianess.Big)
    {
        var span = ReadSpan(4);
        if (end == Endianess.Little) return BinaryPrimitives.ReadSingleLittleEndian(span);

        Span<byte> reversed = stackalloc byte[4];
        span.CopyTo(reversed);
        reversed.Reverse();
        return BinaryPrimitives.ReadSingleLittleEndian(reversed);
    }

    public void WriteFloat(float v, Endianess end = Endianess.Big)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(buffer, v);
        if (end == Endianess.Big) buffer.Reverse();
        Write(buffer);
    }

    public double ReadDouble(Endianess end = Endianess.Big)
    {
        var span = ReadSpan(8);
        if (end == Endianess.Little) return BinaryPrimitives.ReadDoubleLittleEndian(span);

        Span<byte> reversed = stackalloc byte[8];
        span.CopyTo(reversed);
        reversed.Reverse();
        return BinaryPrimitives.ReadDoubleLittleEndian(reversed);
    }

    public void WriteDouble(double v, Endianess end = Endianess.Big)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(buffer, v);
        if (end == Endianess.Big) buffer.Reverse();
        Write(buffer);
    }

    public long ReadLong(Endianess end = Endianess.Big) =>
        end == Endianess.Little ? BinaryPrimitives.ReadInt64LittleEndian(ReadSpan(8)) :
        BinaryPrimitives.ReadInt64BigEndian(ReadSpan(8));

    public void WriteLong(long v, Endianess end = Endianess.Big)
    {
        Span<byte> buffer = stackalloc byte[8];
        if (end == Endianess.Little) BinaryPrimitives.WriteInt64LittleEndian(buffer, v);
        else BinaryPrimitives.WriteInt64BigEndian(buffer, v);
        Write(buffer);
    }

    public ulong ReadULong(Endianess end = Endianess.Big) =>
        end == Endianess.Little ? BinaryPrimitives.ReadUInt64LittleEndian(ReadSpan(8)) :
        BinaryPrimitives.ReadUInt64BigEndian(ReadSpan(8));

    public void WriteULong(ulong v, Endianess end = Endianess.Big)
    {
        Span<byte> buffer = stackalloc byte[8];
        if (end == Endianess.Little) BinaryPrimitives.WriteUInt64LittleEndian(buffer, v);
        else BinaryPrimitives.WriteUInt64BigEndian(buffer, v);
        Write(buffer);
    }

    public int ReadUnsignedVarInt()
    {
        var result = 0;
        var shift = 0;
        byte byteRead;
        do
        {
            if (shift >= 35)
                throw new InvalidOperationException("Unsigned varint is too long (malformed packet).");

            if (Offset >= Length)
                throw new InvalidOperationException("Not enough bytes to read unsigned varint.");

            byteRead = ReadSpan(1)[0];
            result |= (byteRead & 0x7F) << shift;
            shift += 7;
        } while ((byteRead & 0x80) != 0);
        return result;
    }

    public void WriteUnsignedVarInt(int v)
    {
        var value = (uint)v;
        while (value >= 0x80)
        {
            WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        WriteByte((byte)value);
    }

    public long ReadUnsignedVarLong()
    {
        long result = 0;
        var shift = 0;
        byte byteRead;
        do
        {
            if (shift >= 70)
                throw new InvalidOperationException("Unsigned varlong is too long (malformed packet).");

            if (Offset >= Length)
                throw new InvalidOperationException("Not enough bytes to read unsigned varlong.");

            byteRead = ReadSpan(1)[0];
            result |= (long)(byteRead & 0x7F) << shift;
            shift += 7;
        } while ((byteRead & 0x80) != 0);
        return result;
    }

    public void WriteUnsignedVarLong(long v)
    {
        var value = (ulong)v;
        while (value >= 0x80)
        {
            WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        WriteByte((byte)value);
    }

    public int ReadVarInt() => ZigzagDecode(ReadUnsignedVarInt());

    public void WriteVarInt(int v) => WriteUnsignedVarInt(ZigzagEncode(v));

    public long ReadVarLong() => ZigzagDecode(ReadUnsignedVarLong());

    public void WriteVarLong(long v) => WriteUnsignedVarLong(ZigzagEncode(v));

    public byte[] ReadMagic() => ReadSpan(16).ToArray();

    public void WriteMagic() => Write(RakNetServer.MAGIC);

    private static int ZigzagEncode(int value) => (value << 1) ^ (value >> 31);

    private static long ZigzagEncode(long value) => (value << 1) ^ (value >> 63);

    private static int ZigzagDecode(int value) => (value >> 1) ^ -(value & 1);

    private static long ZigzagDecode(long value) => (value >> 1) ^ -(value & 1);

    public void Dispose()
    {
        _disposed = true;
        Buffer = Array.Empty<byte>();
        Length = 0;
        Offset = 0;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BinaryStream));
    }
}
