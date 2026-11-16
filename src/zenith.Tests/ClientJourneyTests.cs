using Xunit;
using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Network;
using Zenith.World;
using Zenith.Packets;
using Zenith.Server;

namespace Zenith.Tests;

/// <summary>
/// Protocol-level journeys: a scripted client talks to a real <see cref="GameSessionHost"/> through <see cref="ProbeConnection"/>, and
/// the assertions are on what that client would receive and on server state. No transport, no UI: packets in, packets and state out.
/// </summary>
public sealed class ClientJourneyTests
{
    private static string Jwt(string payloadJson)
    {
        static string B64Url(string s) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64Url("{}")}.{B64Url(payloadJson)}.sig";
    }

    private static LoginPacket SelfSignedLogin(string name) => new()
    {
        Protocol = ServerIdentity.ProtocolVersion,
        AuthInfo = new LoginPacket.AuthenticationInfo
        {
            AuthenticationType = LoginPacket.AuthenticationInfo.TypeSelfSigned,
            Token = Jwt("{\"xname\":\"" + name + "\"}")
        },
        ClientDataJwt = Jwt("{\"ThirdPartyName\":\"" + name + "\"}")
    };

    [Fact]
    public void A_client_negotiates_settings_logs_in_and_is_offered_resource_packs()
    {
        var fx = new IntentTestFixture();
        fx.Context.Config.Auth.NormalizeAndValidate(); // what the config loader does at boot
        var client = new ProbeConnection(new GameSessionHost(fx.Context));

        client.Send(new RequestNetworkSettingsPacket { ProtocolVersion = ServerIdentity.ProtocolVersion });
        var settings = client.Drain();
        Assert.Contains(settings, p => p.Id == (int)ProtocolInfo.NETWORK_SETTINGS_PACKET);

        client.Send(SelfSignedLogin("Journey"));
        var afterLogin = client.Drain();

        Assert.Contains(afterLogin, p => p.Id == (int)ProtocolInfo.PLAY_STATUS_PACKET);
        Assert.Contains(afterLogin, p => p.Id == (int)ProtocolInfo.RESOURCE_PACKS_INFO_PACKET);
        Assert.False(client.IsClosed);
        Assert.NotNull(fx.Players.Get("Journey"));
        // A self-signed login is a stable id the client picked itself: never authenticated, so no durable privilege.
        Assert.Equal(Zenith.Gameplay.Permissions.IdentityTrust.SelfAsserted, fx.Players.Get("Journey")!.Trust);
    }

    /// <summary>Everything a client does from first contact until it stands in the world, in the order a real one does it.</summary>
    private static async Task<(IntentTestFixture Fx, ProbeConnection Client, List<ReceivedPacket> Received)> JoinAsync(string name, string gameMode = "Survival")
    {
        var fx = new IntentTestFixture();
        fx.Context.Config.Server.Gamemode = gameMode;
        fx.Context.Config.Auth.NormalizeAndValidate(); // what the config loader does at boot
        var client = new ProbeConnection(new GameSessionHost(fx.Context), name);
        var received = new List<ReceivedPacket>();

        client.Send(new RequestNetworkSettingsPacket { ProtocolVersion = ServerIdentity.ProtocolVersion });
        received.AddRange(client.Drain());
        client.Send(SelfSignedLogin(name));
        received.AddRange(client.Drain());
        client.Send(ClientPacket.ResourcePackResponse(ResourcePackClientResponsePacket.STATUS_HAVE_ALL_PACKS));
        received.AddRange(client.Drain());
        client.Send(ClientPacket.ResourcePackResponse(ResourcePackClientResponsePacket.STATUS_COMPLETED));
        received.AddRange(client.Drain());
        client.Send(new RequestChunkRadiusPacket { Radius = 4, MaxRadius = 4 });

        var streaming = new Zenith.Session.ChunkStreamSystem(fx.World);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!received.Any(p => p.Id == (int)ProtocolInfo.PLAY_STATUS_PACKET && p.Body.Length > 0 && IsPlayerSpawn(p)) &&
               DateTime.UtcNow < deadline)
        {
            fx.Clock.AdvanceBy(1);
            streaming.Tick(fx.Clock, fx.Players.Online);
            received.AddRange(client.Drain());
            await Task.Delay(5);
        }

        return (fx, client, received);
    }

    private static bool IsPlayerSpawn(ReceivedPacket packet) =>
        packet.Id == (int)ProtocolInfo.PLAY_STATUS_PACKET &&
        System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(packet.Body) == PlayStatusPacket.PlayerSpawn;

    [Fact]
    public async Task A_client_receives_the_world_and_the_spawn_signal_after_requesting_a_view_distance()
    {
        var (fx, client, received) = await JoinAsync("Spawner");

        Assert.Contains(received, p => p.Id == (int)ProtocolInfo.START_GAME_PACKET);
        Assert.Contains(received, p => p.Id == (int)ProtocolInfo.LEVEL_CHUNK_PACKET);
        Assert.Contains(received, IsPlayerSpawn);
        Assert.False(client.IsClosed, string.Join(Environment.NewLine, fx.Log.Lines.Take(40)));
    }

    [Fact]
    public async Task A_creative_client_breaks_a_block_and_is_told_the_cell_is_now_air()
    {
        var (fx, client, _) = await JoinAsync("Digger", "Creative");
        var player = fx.Players.Get("Digger")!;
        const int X = 2, Y = -61, Z = 2;
        fx.World.SetBlock(X, Y, Z, Blocks.Stone);

        client.Send(new SetLocalPlayerAsInitializedPacket { ActorRuntimeId = (ulong)player.RuntimeId });
        new Zenith.Session.ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.IsInGame, string.Join(Environment.NewLine, fx.Log.Lines.Where(l => !l.StartsWith("debug"))));
        client.Drain();

        client.Send(ClientPacket.DestroyBlock(X, Y, Z, Held(player, 0)));
        fx.Clock.AdvanceBy(1);
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(World.World.AirRuntimeId, fx.World.GetBlock(X, Y, Z));
        var update = Assert.Single(client.Drain(), p => p.Id == (int)ProtocolInfo.UPDATE_BLOCK_PACKET);
        var body = new Zenith.Binary.BinaryStream(update.Body);
        Assert.Equal((X, Y, Z), (body.ReadVarInt(), body.ReadVarInt(), body.ReadVarInt()));
    }

    /// <summary>The stack as the server last described it to this client, which is what a real client echoes back.</summary>
    private static NetworkItemStack Held(Player.Player player, int slot) =>
        player.Session.Protocol.Inventory.DescribeSlot(player.Inventory, slot);

    private static async Task<(IntentTestFixture Fx, ProbeConnection Client, Player.Player Player)> InGameAsync(string name, string gameMode)
    {
        var (fx, client, _) = await JoinAsync(name, gameMode);
        var player = fx.Players.Get(name)!;
        client.Send(new SetLocalPlayerAsInitializedPacket { ActorRuntimeId = (ulong)player.RuntimeId });
        new Zenith.Session.ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.IsInGame, string.Join(Environment.NewLine, fx.Log.Lines.Where(l => !l.StartsWith("debug"))));
        client.Drain();
        return (fx, client, player);
    }

    private static void TickEdits(IntentTestFixture fx)
    {
        fx.Clock.AdvanceBy(1);
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);
    }

    [Fact]
    public async Task A_client_places_a_block_against_a_face_and_the_cell_and_the_hotbar_follow()
    {
        var (fx, client, player) = await InGameAsync("Builder", "Survival");
        const int X = 2, Y = -61, Z = 2;
        fx.World.SetBlock(X, Y, Z, Blocks.Stone);
        Assert.True(player.Inventory.TrySet(0, StackId.FromBlock(Blocks.Stone), 5));
        var before = player.Inventory.Get(0).Count;

        client.Send(ClientPacket.ClickBlock(X, Y, Z, face: 1, Held(player, 0)));
        TickEdits(fx);

        Assert.Equal(Blocks.Stone, fx.World.GetBlock(X, Y + 1, Z));
        Assert.Equal(before - 1, player.Inventory.Get(0).Count);
        Assert.Contains(client.Drain(), p => p.Id == (int)ProtocolInfo.UPDATE_BLOCK_PACKET);
    }

    [Theory]
    [InlineData("Creative")]
    [InlineData("Survival")]
    public async Task A_client_places_a_chest_and_breaks_it_again(string gameMode)
    {
        var (fx, client, player) = await InGameAsync("Chester", gameMode);
        const int X = 2, Y = -61, Z = 2;
        fx.World.SetBlock(X, Y, Z, Blocks.Stone);
        Assert.True(player.Inventory.TrySet(0, StackId.FromBlock(Blocks.Chest), 3));

        client.Send(ClientPacket.ClickBlock(X, Y, Z, face: 1, Held(player, 0)));
        TickEdits(fx);
        Assert.True(Blocks.IsChest(fx.World.GetBlock(X, Y + 1, Z)), "the chest was not placed");
        client.Drain();

        if (gameMode == "Survival")
        {
            // A survival client digs first (PlayerAuthInput start/continue, which the probe cannot encode): the dig intent is
            // submitted as the handler would, the dig system runs, and the destroy arrives once the mining time has elapsed.
            var need = Blocks.BreakTicks(fx.World.GetBlock(X, Y + 1, Z), default);
            Assert.True(player.Dig.SubmitDigStart(X, Y + 1, Z, fx.Clock.CurrentTick, need));
            new BlockDigSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
            fx.Clock.AdvanceBy(need);
        }

        client.Send(ClientPacket.DestroyBlock(X, Y + 1, Z, Held(player, 0)));
        TickEdits(fx);

        Assert.True(Blocks.IsAir(fx.World.GetBlock(X, Y + 1, Z)),
            "the chest is still there after the break: " + string.Join(Environment.NewLine, fx.Log.Lines.TakeLast(12)));
    }

    private static bool CarriesPair(ReceivedPacket p, int x, int z) =>
        p.Id == (int)ProtocolInfo.BLOCK_ACTOR_DATA_PACKET &&
        System.Text.Encoding.ASCII.GetString(p.Body.ToArray()).Contains("pairx") &&
        new Zenith.Binary.BinaryStream(p.Body) is var body && body.ReadVarInt() == x && body.ReadVarInt() is var _ && body.ReadVarInt() == z;

    [Fact]
    public async Task Two_adjacent_chests_are_announced_to_the_client_as_one_double_chest_and_split_when_one_breaks()
    {
        var (fx, client, player) = await InGameAsync("Doubler", "Creative");
        const int Y = -61;
        fx.World.SetBlock(2, Y, 2, Blocks.Stone);
        fx.World.SetBlock(3, Y, 2, Blocks.Stone);
        Assert.True(player.Inventory.TrySet(0, StackId.FromBlock(Blocks.Chest), 4));

        client.Send(ClientPacket.ClickBlock(2, Y, 2, face: 1, Held(player, 0)));
        TickEdits(fx);
        var first = client.Drain();
        Assert.Contains(first, p => p.Id == (int)ProtocolInfo.BLOCK_ACTOR_DATA_PACKET);
        Assert.DoesNotContain(first, p => CarriesPair(p, 2, 2));

        client.Send(ClientPacket.ClickBlock(3, Y, 2, face: 1, Held(player, 0)));
        TickEdits(fx);
        var second = client.Drain();
        Assert.Contains(second, p => CarriesPair(p, 3, 2));
        Assert.Contains(second, p => CarriesPair(p, 2, 2)); // the first one learns it has a partner now

        client.Send(ClientPacket.DestroyBlock(3, Y + 1, 2, Held(player, 0)));
        TickEdits(fx);
        var afterBreak = client.Drain();
        Assert.True(Blocks.IsAir(fx.World.GetBlock(3, Y + 1, 2)));
        Assert.Contains(afterBreak, p => p.Id == (int)ProtocolInfo.BLOCK_ACTOR_DATA_PACKET);
        Assert.DoesNotContain(afterBreak, p => CarriesPair(p, 2, 2)); // back to a single chest
    }
}
