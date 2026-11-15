using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

// Own diagnostic client for the installed core's local framed IPC, protocol 2225.
// This proves delivery through Cinnabar's transport, not rendered terrain admission.
if (args.Length is < 3 or > 4 || (args.Length == 4 && args[3] is not ("--containers" or "--drop")))
{
    Console.Error.WriteLine("Usage: cinnabar-terrain-probe <bedrock-core.exe> <address> <output-directory> [--containers|--drop]");
    return 2;
}
var output = Path.GetFullPath(args[2]); Directory.CreateDirectory(output);
var socketDir = Path.Combine(output, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(socketDir);
var info = new ProcessStartInfo(Path.GetFullPath(args[0]))
{
    UseShellExecute = false, CreateNoWindow = true,
    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
};
foreach (var value in new[] { "-socket-dir", socketDir, "-upstream", args[1] }) info.ArgumentList.Add(value);
using var core = Process.Start(info) ?? throw new IOException("Core did not start.");
var errors = core.StandardError.ReadToEndAsync(); var stdout = core.StandardOutput.ReadToEndAsync();
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
var ct = deadline.Token;
var watch = Stopwatch.StartNew();
var stages = new List<object>();
var loaded = new HashSet<(int X, int Z)>();
var all = new HashSet<(int X, int Z)>();
var arrivals = new Dictionary<(int X, int Z), int>();
var publisher = (X: int.MinValue, Z: int.MinValue, Radius: -1);
ulong actor = 0, inputTick = 0;
var eyeY = 0f; var initialized = false; var spawn = false;
var radiusReplies = 0; var packetCount = 0;
var containersMode = args.Length == 4 && args[3] == "--containers";
var dropMode = args.Length == 4 && args[3] == "--drop";
var gameplayMode = containersMode || dropMode;
var containers = new ContainerState();
var dropped = new DropState();
try
{
    var endpoint = Path.Combine(socketDir, "session.addr");
    while (!File.Exists(endpoint))
    {
        if (core.HasExited) throw new IOException("Core exited before IPC was ready.");
        await Task.Delay(50, ct);
    }
    string[]? address = null;
    while (address is null)
    {
        if (core.HasExited) throw new IOException("Core exited before publishing its endpoint.");
        try
        {
            var candidate = (await File.ReadAllTextAsync(endpoint, ct)).Trim().Split(':');
            if (candidate.Length == 2 && int.TryParse(candidate[1], out _)) address = candidate;
        }
        catch (IOException) { /* Windows can observe creation while the core still holds its write handle. */ }
        if (address is null) await Task.Delay(50, ct);
    }
    using var tcp = new TcpClient();
    await tcp.ConnectAsync(address[0], int.Parse(address[1], System.Globalization.CultureInfo.InvariantCulture), ct);
    var stream = tcp.GetStream();
    await SendFrame(1, JsonSerializer.SerializeToUtf8Bytes(new
    {
        protocol = 2225, client_cache = false,
        client_data = new
        {
            ThirdPartyName = gameplayMode ? "GProbe" + Guid.NewGuid().ToString("N")[..5] : "TerrainProbe", ThirdPartyNameOnly = true, ClientRandomId = 910245L,
            DeviceOS = 7, DeviceId = Guid.NewGuid().ToString(), DeviceModel = "Zenith terrain probe",
            GameVersion = "1.26.60", LanguageCode = "en_US", CurrentInputMode = 1, DefaultInputMode = 1, GuiScale = -1,
            SkinId = "ZenithTerrainProbe", SkinImageWidth = 64, SkinImageHeight = 64,
            SkinData = Convert.ToBase64String(new byte[64 * 64 * 4]),
            SkinResourcePatch = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"geometry\":{\"default\":\"geometry.humanoid.custom\"}}")),
            SkinGeometryData = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}")),
            AnimatedImageData = Array.Empty<object>(), PersonaSkin = false, TrustedSkin = true
        }
    }));
    await AwaitView("join", 0, 0, 4);
    if (containersMode) await TestContainers();
    if (dropMode) await TestDrop();
    foreach (var point in gameplayMode ? Array.Empty<(int, int)>() : new[] { (-1, 0), (-1, -1), (0, -1), (1, -1), (2, -1), (0, 0) })
    {
        await SendPacket(AuthInput(point.Item1 * 16 + .5f, eyeY, point.Item2 * 16 + .5f, ++inputTick));
        await AwaitView("move", point.Item1, point.Item2, 4);
    }
    if (!gameplayMode)
    {
        await SendPacket(new Wire(0x45).Z(2).B(32).Bytes());
        await AwaitView("shrink", 0, 0, 2);
        await SendPacket(new Wire(0x45).Z(4).B(32).Bytes());
        await AwaitView("expand", 0, 0, 4);
        if (radiusReplies < 4) throw new IOException($"Missing radius replies: expected at least 4, received {radiusReplies}.");
    }
    var result = new
    {
        success = true, evidence = gameplayMode ? "core-gameplay-roundtrip" : "core-delivery-only", elapsedMs = watch.ElapsedMilliseconds,
        uniqueColumns = all.Count, repeatedColumns = arrivals.Count(kv => kv.Value > 1), radiusReplies,
        stages
    };
    var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(Path.Combine(output, "result.json"), json, ct);
    Console.WriteLine(json);

    async Task TestDrop()
    {
        await SendPacket(AuthInput(128.5f, eyeY, 128.5f, ++inputTick));
        await AwaitView("drop-area", 8, 8, 4);
        var held = containers.Get(28, 0); dropped.ExpectedItem = held.Id;
        var w = new Wire(0x1e).Z(-2).B(1).U(0).U(0).U(2);
        w.U(0).B(1).B(0).B(0).U(0).Raw(held.Encode()).Raw((held with { Count = held.Count - 1 }).Encode());
        w.U(2).B(0).B(1).U(0).U(0).Raw(ProbeItem.Empty.Encode()).Raw((held with { Count = 1, StackId = 0 }).Encode());
        await SendPacket(w.Bytes());
        await WaitDrop("launch", () => dropped.RuntimeId != 0 && containers.Get(28, 0).Count == held.Count - 1);
        if (dropped.Spawn.Y < eyeY - .5f || dropped.Velocity.Z < .35f) throw new IOException("Drop did not launch from the hand forward.");
        await WaitDrop("flight/landing", () => dropped.Moves >= 10 && dropped.OnGround);
        if (dropped.Position.Z < 130.5f) throw new IOException("Drop did not travel forward.");
        await Task.Delay(2200, ct);
        await SendPacket(AuthInput(dropped.Position.X, eyeY, dropped.Position.Z, ++inputTick));
        await WaitDrop("pickup at landing", () => dropped.Taken && dropped.Removed && containers.Get(28, 0).Count == held.Count);
        stages.Add(new { stage = "q-drop-flight-pickup", launchY = dropped.Spawn.Y,
            forwardTravel = dropped.Position.Z - dropped.Spawn.Z, absoluteMoves = dropped.Moves, restoredCount = held.Count });

        async Task WaitDrop(string label, Func<bool> condition)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(8));
            try { while (!condition()) await Receive(timeout.Token); }
            catch (OperationCanceledException) { throw new IOException("Drop stage timed out: " + label); }
        }
    }

    async Task TestContainers()
    {
        var requestId = -201;
        const int baseX = 80, baseZ = 64;
        var ground = (int)MathF.Round(eyeY - 1.62f);
        await SendPacket(AuthInput(baseX + .5f, eyeY, baseZ + .5f, ++inputTick));
        await AwaitView("container-area", baseX / 16, baseZ / 16, 4);
        var targetZ = Enumerable.Range(baseZ, 3).FirstOrDefault(z =>
            !containers.Blocks.ContainsKey((baseX + 2, ground, z)) && !containers.Blocks.ContainsKey((baseX + 2, ground, z + 1)), int.MinValue);
        if (targetZ == int.MinValue) throw new IOException($"No clear container test cells near ({baseX},{baseZ}); refusing to replace existing blocks.");
        var targetX = baseX + 2;
        var opened = containers.OpenCount;
        await SendPacket(new Wire(0x21).B(6).U(actor).B(0).Bytes());
        await WaitFor("open inventory", () => containers.OpenCount > opened && containers.Type == 255);
        for (byte i = 0; i < 4; i++) await Transfer(28, 2, 13, (byte)(28 + i), 1);
        await Craft("minecraft:WorkBench_recipeId");
        await Transfer(60, 50, 28, 6, 1);
        await Close();
        await Place(targetX, targetZ);
        stages.Add(new { stage = "table-placed", x = targetX, y = ground, z = targetZ, remaining = containers.Get(28, 6).Count });
        opened = containers.OpenCount;
        await Click(targetX, ground, targetZ, 8);
        await WaitFor("open crafting table", () => containers.OpenCount > opened && containers.Window == 3 && containers.Type == 1);
        foreach (var cell in new byte[] { 0, 1, 2, 3, 5, 6, 7, 8 }) await Transfer(28, 2, 13, (byte)(32 + cell), 1);
        await Craft("minecraft:Chest_recipeId");
        await Transfer(60, 50, 28, 6, 1);
        stages.Add(new { stage = "table-open-grid-and-craft", outputCount = containers.Get(28, 6).Count });
        await Close();
        opened = containers.OpenCount; var tableUpdates = containers.UiUpdates;
        await Click(targetX, ground, targetZ, 8);
        await WaitFor("reopen table", () => containers.OpenCount > opened && containers.Type == 1 && containers.UiUpdates > tableUpdates);
        if (Enumerable.Range(32, 9).Any(slot => containers.Get(13, (byte)slot).Count != 0)) throw new IOException("Table reopen restored consumed ingredients.");
        await Close();
        await Place(targetX, targetZ + 1);
        stages.Add(new { stage = "chest-placed", x = targetX, y = ground, z = targetZ + 1, remaining = containers.Get(28, 6).Count });
        opened = containers.OpenCount;
        await Click(targetX, ground, targetZ + 1, 8);
        await WaitFor("open chest", () => containers.OpenCount > opened && containers.Window == 2 && containers.Type == 0 && containers.ChestSlotCount == 27);
        var beforeDirt = containers.Get(28, 1).Count;
        await Transfer(28, 1, 7, 0, 3);
        if (containers.Get(7, 0).Count != 3 || containers.Get(28, 1).Count != beforeDirt - 3) throw new IOException("Deposit did not conserve dirt.");
        await Transfer(7, 0, 28, 1, 3);
        if (containers.Get(7, 0).Count != 0 || containers.Get(28, 1).Count != beforeDirt) throw new IOException("Withdrawal did not conserve dirt.");
        await Close(); opened = containers.OpenCount; var chestUpdates = containers.ChestUpdates;
        await Click(targetX, ground, targetZ + 1, 8);
        await WaitFor("reopen chest", () => containers.OpenCount > opened && containers.Window == 2 && containers.ChestUpdates > chestUpdates);
        if (containers.Get(7, 0).Count != 0) throw new IOException("Reopen resurrected chest contents.");
        await Close();
        stages.Add(new { stage = "chest-open-deposit-withdraw-close-reopen", slots = containers.ChestSlotCount, returnedDirt = beforeDirt });

        async Task WaitFor(string label, Func<bool> condition)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            try { while (!condition()) await Receive(timeout.Token); }
            catch (OperationCanceledException) { throw new IOException("Container stage timed out: " + label); }
        }
        async Task Request(Wire actions, int count)
        {
            var id = requestId; requestId -= 2;
            await SendPacket(new Wire(0x93).U(1).Z(id).U((uint)count).Raw(actions.Bytes()).U(0).I(0).Bytes());
            await WaitFor("stack response " + id, () => containers.Responses.ContainsKey(id));
            if (containers.Responses[id] != 0) throw new IOException($"Server refused container request {id}: status {containers.Responses[id]}.");
        }
        async Task Transfer(byte from, byte fromSlot, byte to, byte toSlot, byte count)
        {
            var w = new Wire().U(1).B(1).B(count);
            Slot(w, from, fromSlot); Slot(w, to, toSlot);
            containers.PredictTransfer(from, fromSlot, to, toSlot, count);
            await Request(w, 1);
        }
        void Slot(Wire w, byte container, byte slot) => w.B(container).B(0).B(slot).I(containers.Get(container, slot).StackId);
        async Task Craft(string recipe)
        {
            var updates = containers.UiUpdates;
            var atTable = containers.Window == 3;
            // Table transactions project counts/stack IDs via ISR, not a full UI snapshot.
            // This scenario's known chest output has the same identity as starter slot 5.
            if (atTable) containers.PredictCraftedChest();
            await Request(new Wire().U(10).B(12).U(ContainerState.Recipe(recipe)).B(1).U(6).B(6).B(0), 2);
            await WaitFor("crafted output " + recipe, () => (atTable || containers.UiUpdates > updates) && containers.Get(60, 50).Id != 0 && containers.Get(60, 50).Count == 1);
        }
        async Task Close()
        {
            var closed = containers.CloseCount;
            await SendPacket(new Wire(0x2f).B((byte)containers.Window).B((byte)containers.Type).B(0).Bytes());
            await WaitFor("close window", () => containers.CloseCount > closed);
        }
        async Task Place(int x, int z)
        {
            var held = containers.Get(28, 6);
            var isChest = held.Id == containers.Get(28, 5).Id;
            await Click(x, ground - 1, z, 6);
            // Chests acquire cardinal state on placement. The following 27-slot open validates
            // the material; exact item runtime equality would reject a correct rotated chest.
            await WaitFor("place block", () => containers.Blocks.TryGetValue((x, ground, z), out var actual) &&
                (isChest || actual == held.BlockId) && containers.Get(28, 6).Count == held.Count - 1);
        }
        Task Click(int x, int y, int z, int hotbar)
        {
            var w = new Wire(0x1e).Z(0).B(0).U(2).U(0).Z(0).B(0).Z(x).Z(y).Z(z).B(1).Z(hotbar).B(0)
                .Raw(containers.Get(28, (byte)hotbar).Encode()).F(baseX + .5f).F(eyeY).F(baseZ + .5f)
                .F(.5f).F(1).F(.5f).U(0).B(0).B(0);
            return SendPacket(w.Bytes());
        }
    }

    async Task AwaitView(string stage, int x, int z, int radius)
    {
        using var stageDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stageDeadline.CancelAfter(TimeSpan.FromSeconds(15));
        var expected = new HashSet<(int X, int Z)>();
        for (var cx = x - radius; cx <= x + radius; cx++)
        for (var cz = z - radius; cz <= z + radius; cz++) expected.Add((cx, cz));
        try
        {
            while (!initialized || publisher != (x, z, radius) || !expected.IsSubsetOf(loaded))
                await Receive(stageDeadline.Token);
        }
        catch (OperationCanceledException)
        {
            var missing = expected.Except(loaded).Select(p => new[] { p.X, p.Z }).ToArray();
            throw new IOException($"Stage {stage} @ {x},{z} radius {radius}: missing {missing.Length}, publisher={publisher}; " +
                JsonSerializer.Serialize(missing));
        }
        stages.Add(new { stage, x, z, radius, received = expected.Count, packetCount, elapsedMs = watch.ElapsedMilliseconds });
    }

    async Task Receive(CancellationToken receiveToken)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, receiveToken);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 1 or > 67108864) throw new IOException("Invalid IPC frame size.");
        var frame = new byte[length]; await stream.ReadExactlyAsync(frame, receiveToken);
        if (frame[0] == 6) throw new IOException("Core disconnected: " + Encoding.UTF8.GetString(frame, 1, length - 1));
        var offset = 1;
        if (frame[0] == 3) offset = checked(5 + BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(1)));
        else if (frame[0] != 2) return;
        while (offset < frame.Length)
        {
            var size = checked((int)ReadU(frame, ref offset));
            var packet = frame.AsSpan(offset, size).ToArray(); offset += size;
            var p = 0; var id = (int)(ReadU(packet, ref p) & 1023); packetCount++;
            if (gameplayMode) containers.Observe(id, packet, p);
            if (dropMode) dropped.Observe(id, packet, p);
            switch (id)
            {
                case 0x0b:
                    _ = ReadU(packet, ref p); actor = ReadU(packet, ref p);
                    await SendPacket(new Wire(0x45).Z(4).B(32).Bytes());
                    break;
                case 0x02:
                    spawn |= BinaryPrimitives.ReadInt32BigEndian(packet.AsSpan(p)) == 3;
                    break;
                case 0x13:
                    if (ReadU(packet, ref p) != actor) break;
                    p += 4; eyeY = BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(p));
                    break;
                case 0x46: radiusReplies++; break;
                case 0x79:
                    var bx = ReadZ(packet, ref p); _ = ReadZ(packet, ref p); var bz = ReadZ(packet, ref p);
                    var r = checked((int)ReadU(packet, ref p)) / 16;
                    publisher = ((int)Math.Floor(bx / 16d), (int)Math.Floor(bz / 16d), r);
                    loaded.RemoveWhere(c => Math.Abs(c.X - publisher.X) > r || Math.Abs(c.Z - publisher.Z) > r);
                    break;
                case 0x3a:
                    var coord = (ReadZ(packet, ref p), ReadZ(packet, ref p));
                    loaded.Add(coord); all.Add(coord); arrivals[coord] = arrivals.GetValueOrDefault(coord) + 1;
                    break;
            }
        }
        if (spawn && !initialized)
        {
            // Exercise 69 while SpawnResponse owns the session, before client initialization.
            await SendPacket(new Wire(0x45).Z(4).B(32).Bytes());
            await SendPacket(new Wire(0x71).U(actor).Bytes());
            initialized = true;
        }
    }

    Task SendPacket(byte[] packet) => SendFrame(2, new Wire().U((ulong)packet.Length).Raw(packet).Bytes());
    async Task SendFrame(byte kind, byte[] body)
    {
        var frame = new byte[5 + body.Length]; BinaryPrimitives.WriteInt32BigEndian(frame, body.Length + 1);
        frame[4] = kind; body.CopyTo(frame, 5); await stream.WriteAsync(frame, ct);
    }
    return 0;
}
catch (Exception exception)
{
    await File.WriteAllTextAsync(Path.Combine(output, "failure.txt"), exception.ToString());
    Console.Error.WriteLine(exception.Message); return 1;
}
finally
{
    core.StandardInput.Close();
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    try { await core.WaitForExitAsync(stop.Token); }
    catch (OperationCanceledException) { core.Kill(entireProcessTree: true); await core.WaitForExitAsync(); }
    await File.WriteAllTextAsync(Path.Combine(output, "core.log"), await errors + await stdout);
}

static byte[] AuthInput(float x, float eyeY, float z, ulong tick)
{
    var w = new Wire(0x90).F(0).F(0).F(x).F(eyeY).F(z).F(0).F(0).F(0);
    w.U(2).Z(37).Z(50); // HandledTeleport and VerticalCollision enum values.
    w.U(1).U(0).Z(0).F(0).F(0).U(tick).F(0).F(0).F(0);
    for (var i = 0; i < 5; i++) w.B(0); // optional interaction/request/actions/vehicle fields
    for (var i = 0; i < 7; i++) w.F(0);
    return w.Bytes();
}
static ulong ReadU(byte[] bytes, ref int offset)
    => Wire.ReadU(bytes, ref offset);
static int ReadZ(byte[] bytes, ref int offset)
{
    var value = checked((uint)ReadU(bytes, ref offset)); return (int)(value >> 1) ^ -(int)(value & 1);
}
sealed class Wire
{
    private readonly MemoryStream _stream = new();
    public Wire(int? id = null) { if (id is { } value) U((ulong)value); }
    public Wire U(ulong value)
    {
        while (value >= 128) { _stream.WriteByte((byte)(value | 128)); value >>= 7; }
        _stream.WriteByte((byte)value); return this;
    }
    public Wire Z(int value) => U((uint)((value << 1) ^ (value >> 31)));
    public Wire B(byte value) { _stream.WriteByte(value); return this; }
    public Wire S(short value) { Span<byte> bytes = stackalloc byte[2]; BinaryPrimitives.WriteInt16LittleEndian(bytes, value); _stream.Write(bytes); return this; }
    public Wire I(int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, value); _stream.Write(bytes); return this; }
    public Wire F(float value)
    {
        Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        _stream.Write(bytes); return this;
    }
    public Wire Raw(byte[] bytes) { _stream.Write(bytes); return this; }
    public byte[] Bytes() => _stream.ToArray();
    public static ulong ReadU(byte[] bytes, ref int offset)
    {
        ulong value = 0;
        for (var shift = 0; shift < 70; shift += 7)
        {
            var b = bytes[offset++]; value |= (ulong)(b & 127) << shift;
            if (b < 128) return value;
        }
        throw new IOException("Invalid varint.");
    }
    public static int ReadZ(byte[] bytes, ref int offset)
    {
        var value = checked((uint)ReadU(bytes, ref offset)); return (int)(value >> 1) ^ -(int)(value & 1);
    }
}
