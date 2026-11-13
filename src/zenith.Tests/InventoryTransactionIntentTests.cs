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

/// <summary>InventoryTransaction, MobEquipment and Interact handlers: decoded on the network thread, applied on tick.</summary>
public class InventoryTransactionIntentTests
{

    [Fact]
    public void MobEquipment_decode_reads_hotbar_slot()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarLong(2); // actor runtime
        // empty-ish network item
        writer.WriteShort(1, BinaryStream.Endianess.Little);
        writer.WriteUShort(1, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(0);
        writer.WriteBool(true);
        writer.WriteVarInt(47); // stack network id
        writer.WriteUnsignedVarInt(0);
        writer.WriteUnsignedVarInt(0);
        writer.WriteByte(0); // inventory slot
        writer.WriteByte(3); // hotbar
        writer.WriteByte(0); // window
        var bytes = writer.GetBufferDisposing().ToArray();

        var stream = new BinaryStream(bytes);
        var packet = new MobEquipmentPacket();
        packet.Decode(ref stream);
        Assert.Equal(3, packet.HotbarSlot);
        Assert.Equal(2, packet.ActorRuntimeId);
        Assert.Equal((short)1, packet.Item.NetworkId);
        Assert.Equal((ushort)1, packet.Item.Count);
        Assert.Equal(47, packet.Item.StackNetworkId);
    }


    [Fact]
    public void MobEquipment_changes_main_hand_only_when_the_claimed_slot_matches_authority()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("equip-valid");
        Assert.True(player.Inventory.TrySetBlock(4, Blocks.Air, 0));
        player.SelectedHotbarSlot = 0;

        var writer = new BinaryStream();
        writer.WriteUnsignedVarLong(player.RuntimeId);
        NetworkItemStack.Empty.WriteNetworkItemStackDescriptor(ref writer);
        writer.WriteByte(4); // inventory slot
        writer.WriteByte(4); // hotbar slot
        writer.WriteByte(MobEquipmentPacket.WindowInventory);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.MOB_EQUIPMENT_PACKET },
            ref stream));

        Assert.Equal(4, player.SelectedHotbarSlot);
    }


    [Fact]
    public void MobEquipment_rejects_a_mismatched_held_stack_without_changing_main_hand()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("equip-mismatch");
        Assert.True(player.Inventory.TrySetBlock(4, Blocks.Stone, 1));
        player.SelectedHotbarSlot = 0;

        var writer = new BinaryStream();
        writer.WriteUnsignedVarLong(player.RuntimeId);
        NetworkItemStack.Empty.WriteNetworkItemStackDescriptor(ref writer); // contradicts stone in slot 4
        writer.WriteByte(4);
        writer.WriteByte(4);
        writer.WriteByte(MobEquipmentPacket.WindowInventory);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.MOB_EQUIPMENT_PACKET },
            ref stream));

        Assert.Equal(0, player.SelectedHotbarSlot);
    }


    [Fact]
    public void InventoryTransaction_actor_attack_submits_gameplay_attack_intent()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("actorattack");
        Assert.True(player.Inventory.TrySetBlock(4, Blocks.Air, 0));
        var writer = new BinaryStream();
        writer.WriteVarInt(0); // legacy request id
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUseOnActor);
        writer.WriteUnsignedVarInt(0); // no legacy actions
        writer.WriteUnsignedVarLong(99); // target actor runtime id
        writer.WriteVarInt(InventoryTransactionPacket.ActorAttack);
        writer.WriteVarInt(4); // hotbar slot
        writer.WriteByte(0); // Hand (2225)
        WriteNetworkItemAir(ref writer);
        for (var i = 0; i < 6; i++)
            writer.WriteFloat(0, BinaryStream.Endianess.Little);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        var handled = new InGameSessionHandler().HandleDataPacket(
            player.Session,
        new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream);

        Assert.True(handled);
        Assert.Equal(4, player.SelectedHotbarSlot);
        Assert.False(player.Intents.TryConsumeAttackIntent(100));
        Assert.True(player.Intents.TryConsumeAttackIntent(99));
    }


    [Fact]
    public void InventoryTransaction_actor_attack_damages_the_named_actor_on_the_gameplay_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("actorattack-damage");
        Assert.True(player.Inventory.TrySetBlock(4, Blocks.Air, 0));

        var zombies = new ZombieSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var zombie = zombies.SpawnZombie(player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(zombies.Stores.Identities.TryGet(zombie, out var identity));

        var writer = new BinaryStream();
        writer.WriteVarInt(0); // legacy request id
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUseOnActor);
        writer.WriteUnsignedVarInt(0); // no legacy actions
        writer.WriteUnsignedVarLong(checked((long)identity.ActorRuntimeId));
        writer.WriteVarInt(InventoryTransactionPacket.ActorAttack);
        writer.WriteVarInt(4);
        writer.WriteByte(0); // Hand (2225)
        WriteNetworkItemAir(ref writer);
        for (var i = 0; i < 6; i++)
            writer.WriteFloat(0, BinaryStream.Endianess.Little);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));

        zombies.Tick(fx.Clock, fx.Players.Online);

        Assert.True(zombies.Stores.Health.TryGet(zombie, out var health));
        Assert.Equal(16f, health.State.Current);
    }


    [Fact]
    public void InventoryTransaction_actor_attack_damages_the_named_player_on_the_gameplay_tick()
    {
        var fx = new IntentTestFixture();
        var attacker = fx.AddInGamePlayer("actorattack-player");
        var target = fx.AddInGamePlayer("actorattack-target");
        Assert.True(attacker.Inventory.TrySetBlock(4, Blocks.Air, 0));
        target.PositionX = attacker.PositionX + 1;
        target.PositionZ = attacker.PositionZ;

        var writer = new BinaryStream();
        writer.WriteVarInt(0);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUseOnActor);
        writer.WriteUnsignedVarInt(0);
        writer.WriteUnsignedVarLong(target.RuntimeId);
        writer.WriteVarInt(InventoryTransactionPacket.ActorAttack);
        writer.WriteVarInt(4);
        writer.WriteByte(0); // Hand (2225)
        WriteNetworkItemAir(ref writer);
        for (var i = 0; i < 6; i++)
            writer.WriteFloat(0, BinaryStream.Endianess.Little);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            attacker.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));

        new PlayerMeleeSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(16f, target.Health);
    }


    [Fact]
    public void InventoryTransaction_actor_interact_uses_the_wire_runtime_id_on_the_gameplay_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("actorinteract-runtime");
        var wheat = fx.Context.ItemPalette.Require("minecraft:wheat");
        Assert.True(player.Inventory.TrySetItem(0, wheat, 1));
        player.SelectedHotbarSlot = 0;

        var cows = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var cow = cows.SpawnCow(player.PositionX + 1, player.PositionY, player.PositionZ);
        ref var identity = ref cows.Stores.Identities.GetRef(cow);
        identity.ActorRuntimeId = checked((ulong)(identity.ActorUniqueId + 10_000));
        ref var cowState = ref cows.CowStates.GetRef(cow);
        cowState.BreedCooldownUntilTick = 5_000;
        var held = player.Session.Protocol.Inventory.DescribeSlot(player.Inventory, 0);

        var writer = new BinaryStream();
        writer.WriteVarInt(0);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUseOnActor);
        writer.WriteUnsignedVarInt(0);
        writer.WriteUnsignedVarLong(checked((long)identity.ActorRuntimeId));
        writer.WriteVarInt(InventoryTransactionPacket.ActorInteract);
        writer.WriteVarInt(0);
        writer.WriteByte(0); // Hand (2225)
        held.WriteNetworkItemStackDescriptor(ref writer);
        for (var i = 0; i < 6; i++)
            writer.WriteFloat(0, BinaryStream.Endianess.Little);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));

        cows.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, cows.FeedCount);
        Assert.True(player.Inventory.Get(0).IsEmpty);
    }


    [Fact]
    public void InventoryTransaction_actor_attack_rejects_a_held_item_mismatch_without_changing_selection()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("actorattack-mismatch");
        Assert.True(player.Inventory.TrySetBlock(4, Blocks.Stone, 1));
        player.SelectedHotbarSlot = 0;

        var writer = new BinaryStream();
        writer.WriteVarInt(0);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUseOnActor);
        writer.WriteUnsignedVarInt(0);
        writer.WriteUnsignedVarLong(99);
        writer.WriteVarInt(InventoryTransactionPacket.ActorAttack);
        writer.WriteVarInt(4);
        writer.WriteByte(0); // Hand (2225)
        WriteNetworkItemAir(ref writer); // contradicts the authoritative stone in slot 4
        for (var i = 0; i < 6; i++)
            writer.WriteFloat(0, BinaryStream.Endianess.Little);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));

        Assert.Equal(0, player.SelectedHotbarSlot);
        Assert.False(player.Intents.TryConsumeAttackIntent(99));
    }


    [Fact]
    public void InventoryTransaction_release_updates_hotbar_without_creating_a_second_use_intent()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("release-slot");
        Assert.True(player.Inventory.TrySetBlock(5, Blocks.Air, 0));
        player.SelectedHotbarSlot = 0;
        var writer = new BinaryStream();
        writer.WriteVarInt(0);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemRelease);
        writer.WriteUnsignedVarInt(0);
        writer.WriteVarInt(0); // release
        writer.WriteVarInt(5); // hotbar slot
        WriteNetworkItemAir(ref writer);
        for (var i = 0; i < 3; i++)
            writer.WriteFloat(0, BinaryStream.Endianess.Little);
        writer.WriteByte(0); // Hand (2225)

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));

        Assert.Equal(5, player.SelectedHotbarSlot);
        Assert.False(player.Intents.TryConsumeEatIntent());
        Assert.False(player.Intents.TryConsumeProjectileIntent());
    }


    [Fact]
    public void InventoryTransaction_unknown_typed_actions_do_not_change_selected_hotbar_slot()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("unknown-transaction-action");
        Assert.True(player.Inventory.TrySetBlock(5, Blocks.Stone, 1));
        player.SelectedHotbarSlot = 0;
        var held = player.Session.Protocol.Inventory.DescribeSlot(player.Inventory, 5);

        var use = new BinaryStream();
        use.WriteVarInt(0);
        use.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUse);
        use.WriteUnsignedVarInt(0);
        use.WriteVarInt(99); // unknown UseItem action
        use.WriteByte(0);
        use.WriteVarInt(0);
        use.WriteVarInt(0);
        use.WriteVarInt(0);
        use.WriteByte(0);
        use.WriteVarInt(5);
        use.WriteByte(InventoryTransactionPacket.HandMainhand);
        held.WriteNetworkItemStackDescriptor(ref use);
        for (var i = 0; i < 6; i++) use.WriteFloat(0, BinaryStream.Endianess.Little);
        use.WriteUnsignedVarInt(0);
        use.WriteByte(0);
        use.WriteByte(0);
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref use));
        Assert.Equal(0, player.SelectedHotbarSlot);

        var actor = new BinaryStream();
        actor.WriteVarInt(0);
        actor.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUseOnActor);
        actor.WriteUnsignedVarInt(0);
        actor.WriteUnsignedVarLong(99);
        actor.WriteVarInt(99); // unknown UseItemOnActor action
        actor.WriteVarInt(5);
        actor.WriteByte(0); // Hand (2225)
        held.WriteNetworkItemStackDescriptor(ref actor);
        for (var i = 0; i < 6; i++) actor.WriteFloat(0, BinaryStream.Endianess.Little);
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref actor));
        Assert.Equal(0, player.SelectedHotbarSlot);

        var release = new BinaryStream();
        release.WriteVarInt(0);
        release.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemRelease);
        release.WriteUnsignedVarInt(0);
        release.WriteVarInt(99); // unknown Release action
        release.WriteVarInt(5);
        held.WriteNetworkItemStackDescriptor(ref release);
        for (var i = 0; i < 3; i++) release.WriteFloat(0, BinaryStream.Endianess.Little);
        release.WriteByte(0); // Hand (2225)
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref release));
        Assert.Equal(0, player.SelectedHotbarSlot);
    }


    [Fact]
    public void InventoryTransaction_use_click_air_relays_animation_without_creating_targetless_melee()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("air-swing");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Air, 0));

        var writer = new BinaryStream();
        writer.WriteVarInt(0);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUse);
        writer.WriteUnsignedVarInt(0);
        writer.WriteVarInt(InventoryTransactionPacket.UseClickAir);
        writer.WriteByte(0);
        writer.WriteVarInt(0);
        writer.WriteVarInt(0);
        writer.WriteVarInt(0);
        writer.WriteByte(0);
        writer.WriteVarInt(0);
        writer.WriteByte(InventoryTransactionPacket.HandMainhand);
        WriteNetworkItemAir(ref writer);
        for (var i = 0; i < 6; i++)
            writer.WriteFloat(0, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(0);
        writer.WriteByte(0);
        writer.WriteByte(0);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));

        Assert.False(player.Intents.TryConsumeAttackIntent());
    }


    [Fact]
    public void InventoryTransaction_click_block_rejects_a_stale_clicked_runtime_before_placing()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stale-click-block");
        StandForPlace(player, 10, 64, 10);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 2));
        var held = player.Session.Protocol.Inventory.DescribeSlot(player.Inventory, 0);

        var writer = new BinaryStream();
        writer.WriteVarInt(0);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUse);
        writer.WriteUnsignedVarInt(0);
        writer.WriteVarInt(InventoryTransactionPacket.UseClickBlock);
        writer.WriteByte(0);
        writer.WriteVarInt(9);
        writer.WriteVarInt(64);
        writer.WriteVarInt(10);
        writer.WriteByte(5); // place on the +X face, at 10,64,10
        writer.WriteVarInt(0);
        writer.WriteByte(InventoryTransactionPacket.HandMainhand);
        held.WriteNetworkItemStackDescriptor(ref writer);
        for (var i = 0; i < 6; i++)
            writer.WriteFloat(0, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(Blocks.Stone); // client predicted stone; server has air
        writer.WriteByte(0);
        writer.WriteByte(0);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));

        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(10, 64, 10));
        Assert.Equal(2, player.Inventory.Get(0).Count);
    }


    [Fact]
    public void InventoryTransaction_click_block_accepts_an_unspecified_clicked_runtime()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("unspecified-click-runtime");
        StandForPlace(player, 10, 64, 10);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 2));
        var held = player.Session.Protocol.Inventory.DescribeSlot(player.Inventory, 0);

        var writer = new BinaryStream();
        writer.WriteVarInt(0);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUse);
        writer.WriteUnsignedVarInt(0);
        writer.WriteVarInt(InventoryTransactionPacket.UseClickBlock);
        writer.WriteByte(0);
        writer.WriteVarInt(9);
        writer.WriteVarInt(64);
        writer.WriteVarInt(10);
        writer.WriteByte(5);
        writer.WriteVarInt(0);
        writer.WriteByte(InventoryTransactionPacket.HandMainhand);
        held.WriteNetworkItemStackDescriptor(ref writer);
        for (var i = 0; i < 6; i++)
            writer.WriteFloat(0, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(0); // target state omitted by this client
        writer.WriteByte(0);
        writer.WriteByte(0);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));

        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Stone, fx.World.GetBlock(10, 64, 10));
        Assert.Equal(1, player.Inventory.Get(0).Count);
    }


    [Fact]
    public void InventoryTransaction_offhand_use_is_rejected_without_hotbar_mutation()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("unsupported-offhand");
        StandForPlace(player, 10, 64, 10);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 2));
        player.SelectedHotbarSlot = 4;
        var held = player.Session.Protocol.Inventory.DescribeSlot(player.Inventory, 0);

        var writer = new BinaryStream();
        writer.WriteVarInt(0); // legacy request ID
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUse);
        writer.WriteUnsignedVarInt(0); // inventory actions
        writer.WriteVarInt(InventoryTransactionPacket.UseClickBlock);
        writer.WriteByte(0); // trigger
        writer.WriteVarInt(9);
        writer.WriteVarInt(64);
        writer.WriteVarInt(10);
        writer.WriteByte(5);
        writer.WriteVarInt(0); // hotbar slot
        writer.WriteByte(1); // offhand
        held.WriteNetworkItemStackDescriptor(ref writer);
        for (var i = 0; i < 6; i++)
            writer.WriteFloat(0, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(0);
        writer.WriteByte(0);
        writer.WriteByte(0);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));

        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(4, player.SelectedHotbarSlot);
        Assert.Equal(Blocks.Air, fx.World.GetBlock(10, 64, 10));
        Assert.Equal(2, player.Inventory.Get(0).Count);
    }


    [Fact]
    public void InventoryTransaction_mismatch_resends_authoritative_inventory_view()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("mismatch");
        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }
        while (fx.Transport.CapturedByEndpoint.TryDequeue(out _)) { }

        var writer = new BinaryStream();
        writer.WriteVarInt(0);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeMismatch);
        writer.WriteUnsignedVarInt(0);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));
        FlushRaknet(fx.Players);

        Assert.NotEmpty(fx.Transport.Captured);
    }


    [Fact]
    public void Legacy_normal_transaction_drop_commits_once_on_the_gameplay_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("legacy-drop");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        var before = player.Session.Protocol.Inventory.DescribeSlot(player.Inventory, 0);
        var after = before with { Count = 3 };
        var dropped = before with { Count = 2 };

        var writer = new BinaryStream();
        writer.WriteVarInt(-2); // correlatable legacy request ID
        writer.WriteUnsignedVarInt(0); // no requested slot replay entries
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeNormal);
        writer.WriteUnsignedVarInt(2);
        WriteNormalInventoryAction(
            ref writer,
            InventoryTransactionPacket.SourceContainer,
            windowId: 0,
            slot: 0,
            before,
            after);
        WriteNormalInventoryAction(
            ref writer,
            InventoryTransactionPacket.SourceWorld,
            windowId: null,
            slot: 0,
            NetworkItemStack.Empty,
            dropped);

        var bytes = writer.GetBufferDisposing().ToArray();
        for (var i = 0; i < 2; i++)
        {
            var stream = new BinaryStream(bytes);
            Assert.True(new InGameSessionHandler().HandleDataPacket(
                player.Session,
                new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
                ref stream));
        }

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(3, player.Inventory.Get(0).Count); // duplicate packet must not remove another two
        Assert.Equal(2, fx.World.FloorDrops.Snapshot()
            .Where(drop => drop.Id == StackId.FromBlock(Blocks.Stone))
            .Sum(drop => drop.Count));
    }


    [Fact]
    public void Legacy_normal_transaction_drop_rejects_a_stale_advertised_stack_id()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("legacy-drop-stale");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        var before = player.Session.Protocol.Inventory.DescribeForWire(
            InventorySlotReference.Player(0), player.Inventory.Get(0));
        Assert.True(before.StackNetworkId > 0);
        var stale = before with { StackNetworkId = before.StackNetworkId + 1 };
        var after = before with { Count = 3 };
        var dropped = before with { Count = 2 };

        var writer = new BinaryStream();
        writer.WriteVarInt(-2);
        writer.WriteUnsignedVarInt(0);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeNormal);
        writer.WriteUnsignedVarInt(2);
        WriteNormalInventoryAction(
            ref writer,
            InventoryTransactionPacket.SourceContainer,
            windowId: 0,
            slot: 0,
            stale,
            after);
        WriteNormalInventoryAction(
            ref writer,
            InventoryTransactionPacket.SourceWorld,
            windowId: null,
            slot: 0,
            NetworkItemStack.Empty,
            dropped);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(5, player.Inventory.Get(0).Count);
        Assert.Empty(fx.World.FloorDrops.Snapshot());
    }


    /// <summary>Regression for ADR §139 — interacting with a crafting table opens a real workbench window, distinct from both chest and the personal inventory.</summary>
    [Fact]
    public void Interacting_with_a_crafting_table_opens_and_closes_a_workbench_window()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("workbench-user");
        fx.World.SetBlock(2, 64, 2, Blocks.CraftingTable);
        StandNear(player, 2, 64, 2);
        var sys = fx.CreateInventorySystem();

        Assert.True(player.Windows.SubmitWindowIntent(InventoryWindowIntent.OpenCraftingTable(2, 64, 2)));
        sys.Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Windows.IsAtCraftingTable);
        Assert.True(player.Windows.OpenContainer is not null);
        var session = player.Windows.OpenContainer!.Value;
        Assert.Equal(InventoryContainerMap.WindowTypeWorkbench, session.WindowType);

        Assert.True(player.Windows.SubmitWindowIntent(InventoryWindowIntent.Close(session.WindowId, session.WindowType)));
        sys.Tick(fx.Clock, fx.Players.Online);

        Assert.False(player.Windows.IsAtCraftingTable);
        Assert.Null(player.Windows.OpenContainer);
    }


    [Fact]
    public void Interact_open_inventory_is_applied_by_the_gameplay_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("open-inventory", GameMode.Creative);
        var writer = new BinaryStream();
        writer.WriteByte(InteractPacket.ActionOpenInventory);
        writer.WriteUnsignedVarLong(player.RuntimeId); // target is self
        writer.WriteBool(false); // no interaction position

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        var handled = new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INTERACT_PACKET },
            ref stream);

        Assert.True(handled);
        Assert.False(player.Windows.InventoryWindowOpen); // Session only publishes the intent.

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Windows.InventoryWindowOpen);
        Assert.True(player.Windows.TryGetOpenContainerSession(out var open));
        Assert.Equal(OpenContainerSession.TargetKind.PlayerInventory, open.Target);
        Assert.Equal((byte)InventoryContainerMap.WindowInventory, open.WindowId);
        Assert.Equal(InventoryContainerMap.WindowTypeInventory, open.WindowType);
    }


    [Fact]
    public void Repeated_interact_open_inventory_does_not_reopen_the_player_container()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("repeat-open", GameMode.Creative);

        for (var i = 0; i < 2; i++)
        {
            var writer = new BinaryStream();
            writer.WriteByte(InteractPacket.ActionOpenInventory);
            writer.WriteUnsignedVarLong(player.RuntimeId);
            writer.WriteBool(false);

            var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
            Assert.True(new InGameSessionHandler().HandleDataPacket(
                player.Session,
                new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INTERACT_PACKET },
                ref stream));
        }

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Windows.TryGetOpenContainerSession(out var open));
        Assert.Equal((uint)1, open.Generation);
    }


    [Fact]
    public void Interact_open_inventory_rejects_a_non_self_target()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("open-inventory-forged", GameMode.Creative);
        var writer = new BinaryStream();
        writer.WriteByte(InteractPacket.ActionOpenInventory);
        writer.WriteUnsignedVarLong(player.RuntimeId + 1);
        writer.WriteBool(false);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INTERACT_PACKET },
            ref stream));

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.False(player.Windows.InventoryWindowOpen);
        Assert.False(player.Windows.TryGetOpenContainerSession(out _));
    }
}
