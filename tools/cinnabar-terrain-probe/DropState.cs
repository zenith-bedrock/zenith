using System.Buffers.Binary;

sealed class DropState
{
    public short ExpectedItem { get; set; }
    public ulong RuntimeId { get; private set; }
    public (float X, float Y, float Z) Spawn { get; private set; }
    public (float X, float Y, float Z) Position { get; private set; }
    public (float X, float Y, float Z) Velocity { get; private set; }
    public int Moves { get; private set; }
    public bool OnGround { get; private set; }
    public bool Taken { get; private set; }
    public bool Removed { get; private set; }
    public void Observe(int id, byte[] packet, int p)
    {
        if (ExpectedItem == 0) return;
        switch (id)
        {
            case 0x0f:
                _ = Wire.ReadU(packet, ref p); var rid = Wire.ReadU(packet, ref p);
                var item = ContainerState.ReadItem(packet, ref p);
                var position = Vec(packet, ref p); var velocity = Vec(packet, ref p);
                if (RuntimeId != 0 || item.Id != ExpectedItem || item.Count != 1 || MathF.Abs(position.X - 128.5f) > 1) return;
                RuntimeId = rid; Spawn = Position = position; Velocity = velocity;
                break;
            case 0x12:
                if (Wire.ReadU(packet, ref p) != RuntimeId) return;
                OnGround = (packet[p++] & 1) != 0; Position = Vec(packet, ref p); Moves++;
                break;
            case 0x11: if (Wire.ReadU(packet, ref p) == RuntimeId) Taken = true; break;
            case 0x0e: if (Wire.ReadU(packet, ref p) >> 1 == RuntimeId) Removed = true; break;
        }
    }
    private static (float X, float Y, float Z) Vec(byte[] bytes, ref int offset)
    {
        var x = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset)); offset += 4;
        var y = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset)); offset += 4;
        var z = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset)); offset += 4;
        return (x, y, z);
    }
}
