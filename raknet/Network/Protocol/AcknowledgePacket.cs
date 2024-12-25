using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network.Protocol;

public abstract class AcknowledgePacket : IPacket
{
    public abstract byte Id { get; }

    public const byte RECORD_TYPE_RANGE = 0;
    public const byte RECORD_TYPE_SINGLE = 1;

    public uint[] Sequences = Array.Empty<uint>();

    public Span<byte> Encode()
    {
        var stream = new BinaryStream();
        var payload = new BinaryStream();
        short records = 0;

        Array.Sort(Sequences);

        if (Sequences.Length > 0)
        {
            var pointer = 1;
            var start = Sequences[0];
            var last = Sequences[0];

            while (pointer < Sequences.Length)
            {
                var current = Sequences[pointer++];
                var diff = current - last;
                if (diff == 1)
                {
                    last = current;
                }
                else if (diff > 1)
                {
                    if (start == last)
                    {
                        payload.WriteByte(RECORD_TYPE_SINGLE);
                        payload.WriteTriad(start, BinaryStream.Endianess.Little);
                        start = last = current;
                    }
                    else
                    {
                        payload.WriteByte(RECORD_TYPE_RANGE);
                        payload.WriteTriad(start, BinaryStream.Endianess.Little);
                        payload.WriteTriad(last, BinaryStream.Endianess.Little);
                        start = last = current;
                    }
                    records++;
                }
            }

            if (start == last)
            {
                payload.WriteByte(RECORD_TYPE_SINGLE);
                payload.WriteTriad(start, BinaryStream.Endianess.Little);
            }
            else
            {
                payload.WriteByte(RECORD_TYPE_RANGE);
                payload.WriteTriad(start, BinaryStream.Endianess.Little);
                payload.WriteTriad(last, BinaryStream.Endianess.Little);
            }
            records++;
        }

        stream.WriteShort(records);
        stream.Write(payload.GetBufferDisposing());
        return stream.GetBufferDisposing();
    }

    public void Decode(BinaryStream stream)
    {
        var count = stream.ReadShort();

        for (var i = 0; i < count && !stream.IsEndOfFile && Sequences.Length < 4096; ++i)
        {
            if (stream.ReadByte() == RECORD_TYPE_RANGE)
            {
                var start = stream.ReadTriad(BinaryStream.Endianess.Little);
                var end = stream.ReadTriad(BinaryStream.Endianess.Little);
                if (end + start > 512)
                {
                    end = start + 512;
                }
                for (var c = start; c <= end; ++c)
                {
                    Sequences.Append(c);
                }
            }
            else
            {
                Sequences.Append(stream.ReadTriad(BinaryStream.Endianess.Little));
            }
        }
    }
}