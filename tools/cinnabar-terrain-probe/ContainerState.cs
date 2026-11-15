using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;

sealed record ProbeItem(short Id, int Count, int Meta, int StackId, int BlockId, byte[] Extra)
{
    public static ProbeItem Empty { get; } = new(0, 0, 0, 0, 0, []);
    public byte[] Encode() => new Wire().S(Id).S((short)Count).U((uint)Meta).B(StackId == 0 ? (byte)0 : (byte)1)
        .Raw(StackId == 0 ? [] : new Wire().Z(StackId).Bytes()).U((uint)BlockId).U((ulong)Extra.Length).Raw(Extra).Bytes();
}

sealed class ContainerState
{
    private readonly Dictionary<(byte Container, byte Slot), ProbeItem> _slots = new();
    public Dictionary<int, byte> Responses { get; } = new();
    public Dictionary<(int X, int Y, int Z), int> Blocks { get; } = new();
    public int Window { get; private set; } = -1;
    public int Type { get; private set; } = -1;
    public int OpenCount { get; private set; }
    public int CloseCount { get; private set; }
    public int ChestSlotCount { get; private set; }
    public int ChestUpdates { get; private set; }
    public int UiUpdates { get; private set; }
    public ProbeItem Get(byte container, byte slot) => _slots.GetValueOrDefault(Key(container, slot), ProbeItem.Empty);
    public void PredictCraftedChest() => _slots[(60, 50)] = Get(28, 5) with { Count = 0, StackId = 0 };
    private static (byte, byte) Key(byte container, byte slot) => (container is 12 or 29 ? (byte)28 : container, slot);

    public void PredictTransfer(byte from, byte fromSlot, byte to, byte toSlot, int count)
    {
        var source = Get(from, fromSlot); var destination = Get(to, toSlot);
        if (source.Count < count) throw new IOException($"Probe source {from}/{fromSlot} has {source.Count}, needs {count}.");
        _slots[Key(from, fromSlot)] = source.Count == count ? ProbeItem.Empty : source with { Count = source.Count - count };
        _slots[Key(to, toSlot)] = source with { Count = destination.Count + count, StackId = destination.StackId };
    }

    public void Observe(int id, byte[] packet, int p)
    {
        switch (id)
        {
            case 0x2e:
                Window = packet[p++]; Type = packet[p]; OpenCount++;
                break;
            case 0x2f: Window = -1; Type = -1; CloseCount++; break;
            case 0x15:
                var x = Wire.ReadZ(packet, ref p); var y = Wire.ReadZ(packet, ref p); var z = Wire.ReadZ(packet, ref p);
                Blocks[(x, y, z)] = unchecked((int)Wire.ReadU(packet, ref p));
                break;
            case 0x31:
                var window = (int)Wire.ReadU(packet, ref p); var count = (int)Wire.ReadU(packet, ref p);
                if (window == 2) { ChestSlotCount = count; ChestUpdates++; }
                if (window == 124) UiUpdates++;
                for (var i = 0; i < count; i++)
                {
                    var item = ReadItem(packet, ref p);
                    (byte C, byte S)? slot = window switch
                    {
                        0 => (28, (byte)i), 2 => (7, (byte)i),
                        124 when i == 0 => (59, 0),
                        124 when i is >= 28 and <= 40 => (13, (byte)i),
                        124 when i == 50 => (60, 50), _ => null
                    };
                    if (slot is { } key) _slots[key] = item;
                }
                break;
            case 0x94:
                var responses = (int)Wire.ReadU(packet, ref p);
                for (var i = 0; i < responses; i++)
                {
                    var status = packet[p++]; var request = Wire.ReadZ(packet, ref p); Responses[request] = status;
                    if (packet[p++] == 0) continue;
                    var containers = (int)Wire.ReadU(packet, ref p);
                    for (var c = 0; c < containers; c++)
                    {
                        var container = packet[p++]; if (packet[p++] != 0) p += 4;
                        var slots = (int)Wire.ReadU(packet, ref p);
                        for (var s = 0; s < slots; s++)
                        {
                            var slot = packet[p++]; p++; var amount = packet[p++];
                            var stackId = packet[p++] != 0 ? Wire.ReadZ(packet, ref p) : 0;
                            var textLength = (int)Wire.ReadU(packet, ref p); p += textLength;
                            textLength = (int)Wire.ReadU(packet, ref p); p += textLength;
                            _ = Wire.ReadZ(packet, ref p);
                            _slots[Key(container, slot)] = amount == 0 ? ProbeItem.Empty : Get(container, slot) with { Count = amount, StackId = stackId };
                        }
                    }
                }
                break;
        }
    }

    public static ProbeItem ReadItem(byte[] packet, ref int p)
    {
        var id = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(p)); p += 2;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(p)); p += 2;
        var meta = (int)Wire.ReadU(packet, ref p);
        var stackId = packet[p++] != 0 ? Wire.ReadZ(packet, ref p) : 0;
        var blockId = unchecked((int)Wire.ReadU(packet, ref p));
        var length = (int)Wire.ReadU(packet, ref p); var extra = packet.AsSpan(p, length).ToArray(); p += length;
        return new(id, count, meta, stackId, blockId, extra);
    }

    public static uint Recipe(string name)
    {
        using var source = typeof(ContainerState).Assembly.GetManifestResourceStream("recipes.json.gz")!;
        using var gzip = new GZipStream(source, CompressionMode.Decompress); using var json = JsonDocument.Parse(gzip);
        uint id = 0;
        foreach (var kind in new[] { "shaped", "shapeless" })
        foreach (var row in json.RootElement.GetProperty(kind).EnumerateArray())
        {
            id++;
            if (row.GetProperty("id").GetString() == name) return id;
        }
        throw new IOException("Unknown probe recipe: " + name);
    }
}
