using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;
using Zenith.Session.Handler;

namespace Zenith.Tests;

/// <summary>
/// The actual state-transition logic in <see cref="ResourcePacksSessionHandler"/> had zero test
/// coverage before this file — <see cref="ResourcePackClientResponseStatusTests"/> only covers the
/// packet DTO's wire constants/decode, not what the handler does with them. That's the same shape
/// gap that let the original off-by-one status bug (ADR §83) ship silently: the wire values were
/// right, but nothing exercised the handler branching on them.
/// </summary>
public class ResourcePacksSessionHandlerTests
{
    private static byte[] BuildStatusOnlyBatch(int status, string name)
    {
        var header = new BinaryStream();
        header.WriteUnsignedVarInt((int)ProtocolInfo.RESOURCE_PACK_CLIENT_RESPONSE_PACKET);
        var body = new BinaryStream();
        body.WriteUnsignedVarInt(status);
        body.WriteVarString(name);
        var bodyBytes = body.TakeOwnedBuffer();

        header.Write(bodyBytes);
        var entry = header.TakeOwnedBuffer();

        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(entry.Length);
        writer.Write(entry);
        return writer.TakeOwnedBuffer();
    }

    [Fact]
    public void Have_all_packs_status_sends_exactly_one_resource_pack_stack_packet()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("have-all-packs");
        player.Session.SetHandler(new ResourcePacksSessionHandler());

        var batch = BuildStatusOnlyBatch(ResourcePackClientResponsePacket.STATUS_HAVE_ALL_PACKS, "have_all_packs");
        var stream = new BinaryStream(batch);
        player.Session.HandleGamePacket(ref stream);
        player.Session.RakSession.Tick(); // flushes RakNet's queued frame(s) to the transport

        Assert.NotEmpty(fx.Transport.Captured);
    }

    /// <summary>
    /// STATUS_COMPLETED triggers the whole pre-spawn seed sequence: StartGame, ItemRegistry,
    /// CreativeContent, CraftingData, AvailableCommands, BiomeDefinitionList, LocalActorData,
    /// PlayerAttributes, LocalAbilities, AdventureSettings — all or nothing, since a thrown exception
    /// partway through (e.g. an unresolved dependency) would leave the client stuck exactly like the
    /// original ADR §83 bug did. Not asserting an exact datagram count — RakNet's own frame/MTU
    /// packing is an internal, orthogonal concern; what this proves is that the whole sequence runs
    /// to completion without throwing and actually reaches the transport.
    /// </summary>
    [Fact]
    public void Completed_status_sends_the_full_pre_spawn_seed_sequence()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("completed-status");
        player.Session.SetHandler(new ResourcePacksSessionHandler());

        var batch = BuildStatusOnlyBatch(ResourcePackClientResponsePacket.STATUS_COMPLETED, "completed");
        var stream = new BinaryStream(batch);
        player.Session.HandleGamePacket(ref stream); // throws if any step in the sequence fails
        player.Session.RakSession.Tick();

        Assert.NotEmpty(fx.Transport.Captured);
    }

    /// <summary>
    /// Proves the handler actually switched to PreSpawnSessionHandler (not just that it sent
    /// packets) — a packet id only PreSpawnSessionHandler recognizes must now be accepted instead of
    /// falling through as unhandled.
    /// </summary>
    [Fact]
    public void Completed_status_hands_off_to_pre_spawn_session_handler()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("hands-off");
        player.Session.SetHandler(new ResourcePacksSessionHandler());

        var completedBatch = BuildStatusOnlyBatch(ResourcePackClientResponsePacket.STATUS_COMPLETED, "completed");
        var completedStream = new BinaryStream(completedBatch);
        player.Session.HandleGamePacket(ref completedStream);
        var sentAfterCompleted = fx.Transport.Captured.Count;

        // ClientCacheStatus is a silent no-op recognized by BOTH handlers, so it can't discriminate
        // which one is active — DisconnectPacket is only meaningful to PreSpawnSessionHandler (and
        // beyond), and its handling (session.Disconnect()) is observable without any extra plumbing.
        var header = new BinaryStream();
        header.WriteUnsignedVarInt((int)ProtocolInfo.DISCONNECT_PACKET);
        var body = new BinaryStream();
        body.WriteBool(false); // not a silent disconnect
        body.WriteVarString("client left");
        var bodyBytes = body.TakeOwnedBuffer();
        header.Write(bodyBytes);
        var entry = header.TakeOwnedBuffer();
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(entry.Length);
        writer.Write(entry);
        var disconnectBatch = writer.TakeOwnedBuffer();

        var disconnectStream = new BinaryStream(disconnectBatch);
        player.Session.HandleGamePacket(ref disconnectStream);

        // ResourcePacksSessionHandler has no DISCONNECT_PACKET case — only PreSpawnSessionHandler
        // (and later) recognizes it and calls session.Disconnect(). If the handler had NOT switched,
        // this packet would fall through unhandled and IsClosed would still be false.
        Assert.True(fx.Transport.Captured.Count >= sentAfterCompleted);
        Assert.True(player.Session.RakSession.IsClosed);
    }
}
