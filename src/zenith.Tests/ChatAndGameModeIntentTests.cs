using System.Collections.Concurrent;
using System.Net;
using Zenith.Ecs;
using Zenith.Event;
using Zenith.Gameplay.Runtime;
using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Session;
using Zenith.Session.Handler;
using Zenith.Player;
using Zenith.Raknet;
using Zenith.Logging;
using Zenith.Raknet.Network;
using Zenith.Binary;
using Zenith.Server;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction;
using static Zenith.Tests.IntentTestHelpers;

namespace Zenith.Tests;

/// <summary>Chat and game-mode intents: validated on the network thread, applied on tick.</summary>
public class ChatAndGameModeIntentTests
{

    [Fact]
    public void Chat_pending_is_consumed_by_ChatSystem_tick()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice");
        _ = fx.AddInGamePlayer("bob");

        Assert.True(alice.Intents.SubmitChat("hello"));
        Assert.True(alice.Intents.TryConsumeChat(out var msg));
        Assert.Equal("hello", msg);

        Assert.True(alice.Intents.SubmitChat("hello"));
        var before = fx.Transport.Captured.Count;
        new ChatSystem().Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.False(alice.Intents.TryConsumeChat(out _));
        Assert.True(fx.Transport.Captured.Count > before);
    }


    [Fact]
    public void Chat_fifo_drains_all_pending_on_tick()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice");
        _ = fx.AddInGamePlayer("bob");

        Assert.True(alice.Intents.SubmitChat("one"));
        Assert.True(alice.Intents.SubmitChat("two"));
        Assert.True(alice.Intents.SubmitChat("three"));
        new ChatSystem().Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);
        Assert.False(alice.Intents.TryConsumeChat(out _));
    }


    [Fact]
    public void Chat_fifo_rejects_when_full()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice");
        for (var i = 0; i < Player.PlayerIntents.MaxPendingChat; i++)
            Assert.True(alice.Intents.SubmitChat($"m{i}"));
        Assert.False(alice.Intents.SubmitChat("overflow"));
    }


    [Fact]
    public void ChatSystem_preserves_pending_chat_until_player_is_in_game()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("pre-spawn", isInGame: false);
        Assert.True(player.Intents.SubmitChat("not-yet"));

        new ChatSystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Intents.TryConsumeChat(out var message));
        Assert.Equal("not-yet", message);
    }


    [Fact]
    public void ChatSystem_fans_out_on_tick_without_handler_path()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice");
        var bob = fx.AddInGamePlayer("bob");
        alice.Session.Profile = new ClientProfile("25332747913222912", "dev", 7, "plat");

        alice.Intents.SubmitChat("ping");
        var datagramsBefore = fx.Transport.Captured.Count;

        // InGameSessionHandler only SubmitChat; fan-out is ChatSystem's job.
        new ChatSystem().Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.False(alice.Intents.TryConsumeChat(out _));
        Assert.True(fx.Transport.Captured.Count > datagramsBefore,
            "ChatSystem should SendChat to in-game peers via RakNet.");

        var joined = ConcatCaptured(fx);
        Assert.Contains("25332747913222912"u8.ToArray(), joined);

        bob.Intents.SubmitChat("pong");
        new ChatSystem().Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);
        Assert.False(bob.Intents.TryConsumeChat(out _));
    }


    [Theory]
    [InlineData("survival", 0)]
    [InlineData("creative", 1)]
    [InlineData("s", 0)]
    [InlineData("c", 1)]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("SURVIVAL", 0)]
    public void GameModeConfig_parses_known_args(string arg, int expected)
    {
        Assert.True(GameModeConfig.TryParseArg(arg, out var mode));
        Assert.Equal((GameMode)expected, mode);
    }


    [Theory]
    [InlineData("/gamemode creative", 1, false)]
    [InlineData("/gamemode s", 0, false)]
    [InlineData("/GAMEMODE 1", 1, false)]
    [InlineData("gamemode creative", 1, false)]
    [InlineData("gamemode c", 1, false)]
    public void GameModeConfig_parses_command_line(string line, int expected, bool expectBad)
    {
        var ok = GameModeConfig.TryParseCommand(line, out var mode, out var bad);
        Assert.Equal(!expectBad, ok);
        Assert.Equal(expectBad, bad);
        if (ok) Assert.Equal((GameMode)expected, mode);
    }


    [Theory]
    [InlineData("/gamemode")]
    [InlineData("/gamemode xyz")]
    [InlineData("/gamemode adventure")]
    public void GameModeConfig_bad_gamemode_args_set_badArgs(string line)
    {
        Assert.False(GameModeConfig.TryParseCommand(line, out _, out var bad));
        Assert.True(bad);
    }


    [Theory]
    [InlineData("/help")]
    [InlineData("/tp 0 0 0")]
    [InlineData("hello")]
    public void GameModeConfig_non_gamemode_lines_are_not_commands(string line)
    {
        Assert.False(GameModeConfig.TryParseCommand(line, out _, out var bad));
        Assert.False(bad);
    }


    [Fact]
    public void GameMode_intent_overwrite_latest_and_system_applies_without_chat_fanout()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice", GameMode.Survival);
        var bob = fx.AddInGamePlayer("bob", GameMode.Survival);
        Assert.True(alice.Inventory.TrySetBlock(0, Blocks.Stone, 3));

        alice.Intents.SubmitGameMode(GameMode.Creative);
        alice.Intents.SubmitGameMode(GameMode.Survival); // overwrite-latest
        alice.Intents.SubmitGameMode(GameMode.Creative);

        var before = fx.Transport.Captured.Count;
        new GameModeSystem().Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.Equal(GameMode.Creative, alice.GameMode);
        Assert.Equal(GameMode.Survival, bob.GameMode);
        Assert.False(alice.Intents.TryConsumeGameMode(out _));
        Assert.Equal(Blocks.Stone, alice.Inventory.Get(0).Id.Value);
        Assert.Equal(3, alice.Inventory.Get(0).Count);
        Assert.True(fx.Transport.Captured.Count > before,
            "GameModeSystem should transmit SetPlayerGameType/abilities to self.");

        // Slash path must not use SubmitChat — peer must not get a chat fan-out from mode switch.
        Assert.False(alice.Intents.TryConsumeChat(out _));
        new ChatSystem().Tick(fx.Clock, fx.Players.Online);
        Assert.False(bob.Intents.TryConsumeChat(out _));
    }


    [Fact]
    public void GameModeSystem_refresh_peer_view_sends_to_other_InGame_peers()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice", GameMode.Survival);
        var bob = fx.AddInGamePlayer("bob", GameMode.Survival);

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        alice.Intents.SubmitGameMode(GameMode.Creative);
        new GameModeSystem().Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.Equal(GameMode.Creative, alice.GameMode);
        Assert.True(fx.Transport.Captured.Count >= 1,
            "GameModeSystem RefreshPeerView should send RemoveActor+AddPlayer toward peers.");
        _ = bob;
    }


    [Fact]
    public void GameModeSystem_remints_creative_content_on_every_mode_change()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("switcher", GameMode.Survival);
        var creativePayload = InventoryProtocol.BuildCreativeContent(
            fx.Context.Creative, fx.Context.ItemPalette).Encode().ToArray();

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        player.Intents.SubmitGameMode(GameMode.Creative);
        new GameModeSystem().Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);
        Assert.Equal(GameMode.Creative, player.GameMode);
        Assert.Contains(ReassembleApplicationPackets(fx), msg => msg.AsSpan().IndexOf(creativePayload) >= 0);

        while (fx.Transport.Captured.TryDequeue(out _)) { }

        player.Intents.SubmitGameMode(GameMode.Survival);
        new GameModeSystem().Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);
        Assert.Equal(GameMode.Survival, player.GameMode);
        Assert.Contains(ReassembleApplicationPackets(fx), msg => msg.AsSpan().IndexOf(creativePayload) >= 0);
    }
}
