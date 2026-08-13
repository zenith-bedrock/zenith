using System.Linq;
using System.Net;
using Zenith.Gameplay;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Systems;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Stream;
using Zenith.Session;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

/// <summary>Phase XI.2 — armor slots, equip/unequip via ISR, damage mitigation, death and persistence.</summary>
public class ArmorTests
{
    public ArmorTests() => Blocks.EnsureLoaded();

    private static short IronHelmet(IntentTestFixture fx) => fx.Context.ItemPalette.Require("minecraft:iron_helmet");
    private static short IronChestplate(IntentTestFixture fx) => fx.Context.ItemPalette.Require("minecraft:iron_chestplate");

    private static void EquipDiamondSet(Player.Player player, IntentTestFixture fx)
    {
        var palette = fx.Context.ItemPalette;
        Assert.True(player.Inventory.TrySetArmor(PlayerInventory.ArmorHelmetSlot,
            StackId.FromItem(palette.Require("minecraft:diamond_helmet")), 1));
        Assert.True(player.Inventory.TrySetArmor(PlayerInventory.ArmorChestplateSlot,
            StackId.FromItem(palette.Require("minecraft:diamond_chestplate")), 1));
        Assert.True(player.Inventory.TrySetArmor(PlayerInventory.ArmorLeggingsSlot,
            StackId.FromItem(palette.Require("minecraft:diamond_leggings")), 1));
        Assert.True(player.Inventory.TrySetArmor(PlayerInventory.ArmorBootsSlot,
            StackId.FromItem(palette.Require("minecraft:diamond_boots")), 1));
    }

    [Fact]
    public void Equipping_a_helmet_via_transfer_moves_it_into_the_matching_armor_slot()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("equipper");
        Assert.True(player.Inventory.TrySetItem(0, IronHelmet(fx), 1));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(1, [
            InventoryStackAction.Transfer(InventorySlotReference.Player(0), InventorySlotReference.Armor(PlayerInventory.ArmorHelmetSlot), 1)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Inventory.Get(0).IsEmpty);
        var equipped = player.Inventory.GetArmor(PlayerInventory.ArmorHelmetSlot);
        Assert.False(equipped.IsEmpty);
        Assert.Equal(IronHelmet(fx), (short)equipped.Id.Value);
    }

    [Fact]
    public void Unequipping_moves_armor_back_into_the_bag()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("unequipper");
        Assert.True(player.Inventory.TrySetArmor(PlayerInventory.ArmorHelmetSlot, StackId.FromItem(IronHelmet(fx)), 1));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(1, [
            InventoryStackAction.Transfer(InventorySlotReference.Armor(PlayerInventory.ArmorHelmetSlot), InventorySlotReference.Player(6), 1)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Inventory.GetArmor(PlayerInventory.ArmorHelmetSlot).IsEmpty);
        Assert.Equal(1, player.Inventory.Get(6).Count);
    }

    [Fact]
    public void Placing_a_helmet_in_the_chestplate_slot_is_rejected_and_rolled_back()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("mismatched");
        Assert.True(player.Inventory.TrySetItem(0, IronHelmet(fx), 1));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(1, [
            InventoryStackAction.Transfer(InventorySlotReference.Player(0), InventorySlotReference.Armor(PlayerInventory.ArmorChestplateSlot), 1)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, player.Inventory.Get(0).Count);
        Assert.True(player.Inventory.GetArmor(PlayerInventory.ArmorChestplateSlot).IsEmpty);
    }

    [Fact]
    public void Swapping_a_non_armor_item_into_an_armor_slot_is_rejected()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("swapper");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 1));
        Assert.True(player.Inventory.TrySetArmor(PlayerInventory.ArmorHelmetSlot, StackId.FromItem(IronHelmet(fx)), 1));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(1, [
            InventoryStackAction.Swap(InventorySlotReference.Player(0), InventorySlotReference.Armor(PlayerInventory.ArmorHelmetSlot))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Dirt, player.Inventory.Get(0).Id.Value);
        Assert.False(player.Inventory.GetArmor(PlayerInventory.ArmorHelmetSlot).IsEmpty);
    }

    [Fact]
    public void Full_diamond_set_mitigates_melee_damage_by_eighty_percent()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("tank");
        EquipDiamondSet(player, fx);

        var applied = PlayerDamage.Apply(player, fx.Players, fx.Players.Online, DamageSource.Melee, 10f);

        Assert.True(applied);
        Assert.Equal(18f, player.Health); // 10 * (1 - 0.8) = 2 damage
    }

    [Fact]
    public void Unarmored_player_takes_full_damage()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("bare");

        _ = PlayerDamage.Apply(player, fx.Players, fx.Players.Online, DamageSource.Melee, 10f);

        Assert.Equal(10f, player.Health);
    }

    [Fact]
    public void Void_damage_bypasses_armor()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("voidtank");
        EquipDiamondSet(player, fx);

        _ = PlayerDamage.Apply(player, fx.Players, fx.Players.Online, DamageSource.Void, player.MaxHealth);

        Assert.True(player.IsDead);
    }

    [Fact]
    public void Death_drops_and_clears_equipped_armor()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("armored-death");
        Assert.True(player.Inventory.TrySetArmor(PlayerInventory.ArmorHelmetSlot, StackId.FromItem(IronHelmet(fx)), 1));

        _ = PlayerDamage.Apply(player, fx.Players, fx.Players.Online, DamageSource.Void, player.MaxHealth);

        Assert.True(player.IsDead);
        Assert.True(player.Inventory.GetArmor(PlayerInventory.ArmorHelmetSlot).IsEmpty);
        Assert.Contains(fx.World.FloorDrops.Snapshot(), d => d.Id == StackId.FromItem(IronHelmet(fx)));
    }

    [Fact]
    public void Respawn_resets_health_but_armor_stays_cleared_from_death()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("respawner");
        Assert.True(player.Inventory.TrySetArmor(PlayerInventory.ArmorHelmetSlot, StackId.FromItem(IronChestplate(fx)), 1));

        _ = PlayerDamage.Apply(player, fx.Players, fx.Players.Online, DamageSource.Void, player.MaxHealth);
        Assert.True(player.IsDead);

        player.SubmitRespawn();
        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.False(player.IsDead);
        Assert.True(player.Inventory.GetArmor(PlayerInventory.ArmorHelmetSlot).IsEmpty);
    }

    [Fact]
    public void Armor_blob_round_trips_through_pack_and_load()
    {
        var inventory = new PlayerInventory(seedStarterHotbar: false);
        Assert.True(inventory.TrySetArmor(PlayerInventory.ArmorHelmetSlot, StackId.FromItem(42), 1));
        Assert.True(inventory.TrySetArmor(PlayerInventory.ArmorBootsSlot, StackId.FromItem(7), 1));

        var blob = inventory.PackArmorBlob();

        var reloaded = new PlayerInventory(seedStarterHotbar: false);
        Assert.True(reloaded.TryLoadArmorFromBlob(blob));
        Assert.Equal(42, reloaded.GetArmor(PlayerInventory.ArmorHelmetSlot).Id.Value);
        Assert.Equal(7, reloaded.GetArmor(PlayerInventory.ArmorBootsSlot).Id.Value);
        Assert.True(reloaded.GetArmor(PlayerInventory.ArmorChestplateSlot).IsEmpty);
    }

    [Fact]
    public void Reconnect_reloads_the_same_uuids_persisted_armor()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("persisted");
        Assert.True(player.Inventory.TrySetArmor(PlayerInventory.ArmorHelmetSlot, StackId.FromItem(IronHelmet(fx)), 1));

        fx.World.PersistArmor(player);

        // A fresh session for the same identity — mirrors LoginSessionHandler's post-TryAdd load,
        // without standing up the full login handshake.
        var reconnected = fx.AddPlayer("persisted-reconnect");
        Assert.True(fx.World.TryLoadArmor(player.Uuid, reconnected.Inventory));

        Assert.Equal(IronHelmet(fx), (short)reconnected.Inventory.GetArmor(PlayerInventory.ArmorHelmetSlot).Id.Value);
    }

    [Fact]
    public void Late_joiner_sees_an_already_online_peers_equipped_armor()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice");
        Assert.True(alice.Inventory.TrySetArmor(PlayerInventory.ArmorHelmetSlot, StackId.FromItem(IronHelmet(fx)), 1));

        var bob = fx.AddInGamePlayer("bob");
        bob.IsInGame = false; // AnnounceJoin runs before InGame OnEnable, same as PlayerVisibilityJoinTests.
        Drain(fx);

        PlayerVisibility.AnnounceJoin(bob, fx.Players.Online);
        FlushRaknet(fx.Players);

        var heads = ReadMobArmorHeads(fx, bob.Session.RakSession.EndPoint);
        Assert.Contains(heads, h => h.ActorRuntimeId == (ulong)alice.RuntimeId && h.HeadNetworkId == IronHelmet(fx));
    }

    [Fact]
    public void Existing_peers_see_a_joiners_equipped_armor()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice2");

        var bob = fx.AddInGamePlayer("bob2");
        bob.IsInGame = false;
        Assert.True(bob.Inventory.TrySetArmor(PlayerInventory.ArmorHelmetSlot, StackId.FromItem(IronHelmet(fx)), 1));
        Drain(fx);

        PlayerVisibility.AnnounceJoin(bob, fx.Players.Online);
        FlushRaknet(fx.Players);

        var heads = ReadMobArmorHeads(fx, alice.Session.RakSession.EndPoint);
        Assert.Contains(heads, h => h.ActorRuntimeId == (ulong)bob.RuntimeId && h.HeadNetworkId == IronHelmet(fx));
    }

    private static void FlushRaknet(PlayerManager players)
    {
        foreach (var player in players.Online)
            player.Session.RakSession.Tick();
    }

    private static void Drain(IntentTestFixture fx)
    {
        while (fx.Transport.Captured.TryDequeue(out _)) { }
        while (fx.Transport.CapturedByEndpoint.TryDequeue(out _)) { }
    }

    /// <summary>Decodes MobArmorEquipment packets (RakNet framing + uncompressed game batch) sent to one recipient.</summary>
    private static List<(ulong ActorRuntimeId, short HeadNetworkId)> ReadMobArmorHeads(IntentTestFixture fx, IPEndPoint recipient)
    {
        var found = new List<(ulong, short)>();
        while (fx.Transport.CapturedByEndpoint.TryDequeue(out var captured))
        {
            if (!captured.EndPoint.Equals(recipient)) continue;

            var datagram = captured.Datagram;
            if (datagram.Length < 2 || (datagram[0] & 0xf0) != (byte)BitFlags.Valid) continue;

            var frameStream = new BinaryStream(datagram[1..]);
            var frameSet = new FrameSet();
            frameSet.Decode(ref frameStream);
            frameStream.Dispose();

            foreach (var frame in frameSet.Packets)
            {
                // A batch containing a large payload (e.g. PlayerListAdd's skin blob) can be
                // split across multiple datagrams; reassembly is out of scope for this decode
                // helper, so skip split fragments — the small MobArmorEquipment packet rides in
                // an unsplit frame regardless.
                if (frame.IsSplit()) continue;

                var gameStream = new BinaryStream(frame.Buffer);
                if (gameStream.ReadByte() != 0xfe || gameStream.ReadByte() != PacketCompression.NONE)
                {
                    gameStream.Dispose();
                    continue;
                }

                while (!gameStream.IsEndOfFile)
                {
                    var length = gameStream.ReadUnsignedVarInt();
                    var packetBytes = gameStream.ReadSpan(length).ToArray();
                    var packetStream = new BinaryStream(packetBytes);
                    if (packetStream.ReadUnsignedVarInt() != (int)ProtocolInfo.MOB_ARMOR_EQUIPMENT_PACKET)
                    {
                        packetStream.Dispose();
                        continue;
                    }

                    var actorRuntimeId = (ulong)packetStream.ReadUnsignedVarLong();
                    var head = ReadNetworkItemDescriptor(ref packetStream);
                    found.Add((actorRuntimeId, head));
                    packetStream.Dispose();
                }

                gameStream.Dispose();
            }
        }

        return found;
    }

    private static short ReadNetworkItemDescriptor(ref BinaryStream stream)
    {
        var networkId = stream.ReadShort(BinaryStream.Endianess.Little);
        _ = stream.ReadUShort(BinaryStream.Endianess.Little); // count
        _ = stream.ReadUnsignedVarInt(); // meta
        if (stream.ReadBool())
            _ = stream.ReadVarInt(); // stack net id
        _ = stream.ReadUnsignedVarInt(); // block runtime id
        var extraLen = stream.ReadUnsignedVarInt();
        if (extraLen > 0) stream.ReadSpan(extraLen);
        return networkId;
    }
}
