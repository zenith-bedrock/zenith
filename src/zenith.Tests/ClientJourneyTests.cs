using Xunit;
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

        client.Send(ClientPacket.DestroyBlock(X, Y, Z));
        fx.Clock.AdvanceBy(1);
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(World.World.AirRuntimeId, fx.World.GetBlock(X, Y, Z));
        var update = Assert.Single(client.Drain(), p => p.Id == (int)ProtocolInfo.UPDATE_BLOCK_PACKET);
        var body = new Zenith.Binary.BinaryStream(update.Body);
        Assert.Equal((X, Y, Z), (body.ReadVarInt(), body.ReadVarInt(), body.ReadVarInt()));
    }
}
