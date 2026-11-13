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

namespace Zenith.Tests;

/// <summary>
/// Helpers shared by the intent-contract test classes (<c>*IntentTests</c>): positioning a player, queuing a break the way the handler
/// would, and building ItemStackRequest / InventoryTransaction wire bytes. The harness itself (IntentTestFixture, RecordingRakNetServer)
/// lives in IntentTestFixture.cs.
/// </summary>
internal static class IntentTestHelpers
{
    internal static void StandNear(Player.Player player, int x, int y, int z)
    {
        player.PositionX = x + 0.5f;
        player.PositionY = y; // feet; eyes = Y + EyeHeight still within reach of block center
        player.PositionZ = z + 0.5f;
    }


    /// <summary>
    /// Feet in the +X adjacent cell so place into (x,y,z) is not self-obstructed (§15).
    /// </summary>
    internal static void StandForPlace(Player.Player player, int x, int y, int z)
    {
        player.PositionX = x + 1.5f;
        player.PositionY = y;
        player.PositionZ = z + 0.5f;
    }


    internal static void BeginBreakReady(GameClock clock, World.World world, Player.Player player, int x, int y, int z)
    {
        var held = player.Inventory.Get(player.SelectedHotbarSlot);
        var heldId = held.IsEmpty ? default : held.Id;
        var need = Blocks.BreakTicks(world.GetBlock(x, y, z), heldId);
        player.Dig.BeginBreak(x, y, z, clock.CurrentTick, need, heldId);
        if (need > 0)
            clock.AdvanceBy(need);
    }


    /// <summary>Queue a Survival break with dig auth frozen into the intent (§27).</summary>
    internal static void QueueReadyBreak(
        GameClock clock,
        World.World world,
        Player.Player player,
        int x,
        int y,
        int z)
    {
        BeginBreakReady(clock, world, player, x, y, z);
        var need = player.Dig.BreakRequiredTicks;
        var intent = need > 0
            ? BlockEditIntent.BreakWithDig(x, y, z, player.Dig.BreakStartedTick, player.Dig.BreakRequiredTicks)
            : BlockEditIntent.Set(x, y, z, Blocks.Air);
        Assert.True(player.Intents.SubmitBlockEdit(intent));
    }


    internal static void EquipWoodenPickaxe(Player.Player player, int slot = 0)
    {
        Tools.EnsureLoaded();
        Assert.True(player.Inventory.TrySetItem(slot, Tools.Require("minecraft:wooden_pickaxe"), 1));
        player.SelectedHotbarSlot = slot;
    }


    internal static void SubmitCraftRecipeRequest(Player.Player player, int requestId, uint recipeNetId)
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1); // request count
        writer.WriteVarInt(requestId);
        writer.WriteUnsignedVarInt(1); // action count
        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionCraftRecipe);
        writer.WriteUnsignedVarInt(checked((int)recipeNetId));
        writer.WriteByte(1); // craft times
        writer.WriteUnsignedVarInt(0); // filter strings
        writer.WriteInt(0, BinaryStream.Endianess.Little); // filter cause

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        var handled = new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.ITEM_STACK_REQUEST_PACKET },
            ref stream);
        Assert.True(handled);
    }


    internal static void SubmitDropRequest(Player.Player player, int requestId, byte containerId, byte slot)
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1); // request count
        writer.WriteVarInt(requestId);
        writer.WriteUnsignedVarInt(1); // action count
        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionDrop);
        writer.WriteByte(1); // count
        WriteSlotInfo(ref writer, containerId, slot);
        writer.WriteBool(false); // randomly
        SubmitItemStackRequest(player, writer);
    }


    internal static void SubmitChestAndCraftRequest(Player.Player player, int requestId, uint recipeNetId)
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1); // request count
        writer.WriteVarInt(requestId);
        writer.WriteUnsignedVarInt(2); // action count
        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionPlace);
        writer.WriteByte(1); // count
        WriteSlotInfo(ref writer, InventoryContainerMap.Hotbar, 0);
        WriteSlotInfo(ref writer, InventoryContainerMap.Chest, 0);
        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionCraftRecipe);
        writer.WriteUnsignedVarInt(checked((int)recipeNetId));
        writer.WriteByte(1); // craft times
        SubmitItemStackRequest(player, writer);
    }


    internal static void SubmitChestTransferRequest(Player.Player player, int requestId)
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1); // request count
        writer.WriteVarInt(requestId);
        writer.WriteUnsignedVarInt(1); // action count
        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionPlace);
        writer.WriteByte(1); // count
        WriteSlotInfo(ref writer, InventoryContainerMap.Hotbar, 0);
        WriteSlotInfo(ref writer, InventoryContainerMap.Chest, 0);
        SubmitItemStackRequest(player, writer);
    }


    internal static void WriteSlotInfo(ref BinaryStream writer, byte containerId, byte slot)
    {
        writer.WriteByte(containerId);
        writer.WriteBool(false); // dynamic container id
        writer.WriteByte(slot);
        writer.WriteInt(0, BinaryStream.Endianess.Little); // stack network id
    }


    internal static void WriteDynamicSlotInfo(ref BinaryStream writer, byte containerId, byte slot, uint dynamicId)
    {
        writer.WriteByte(containerId);
        writer.WriteBool(true);
        writer.WriteUInt(dynamicId, BinaryStream.Endianess.Little);
        writer.WriteByte(slot);
        writer.WriteInt(0, BinaryStream.Endianess.Little);
    }


    internal static void WriteStackActionHeader(ref BinaryStream writer, byte legacyActionId)
    {
        writer.WriteUnsignedVarInt(legacyActionId);
        writer.WriteByte(legacyActionId);
    }


    internal static void SubmitItemStackRequest(Player.Player player, BinaryStream writer)
    {
        writer.WriteUnsignedVarInt(0); // filter strings
        writer.WriteInt(0, BinaryStream.Endianess.Little); // filter cause

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        var handled = new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.ITEM_STACK_REQUEST_PACKET },
            ref stream);
        Assert.True(handled);
    }


    internal static int CountRuntime(PlayerInventory inv, int runtimeId)
    {
        var n = 0;
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
        {
            var s = inv.Get(i);
            if (s.Id.IsBlock && s.Id.Value == runtimeId)
                n += s.Count;
        }
        return n;
    }


    internal static void WriteNetworkItemAir(ref BinaryStream writer)
    {
        writer.WriteShort(0, BinaryStream.Endianess.Little);
        writer.WriteUShort(0, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(0);
        writer.WriteBool(false);
        writer.WriteUnsignedVarInt(0);
        writer.WriteUnsignedVarInt(0);
    }


    internal static void WriteNormalInventoryAction(
        ref BinaryStream writer,
        uint sourceType,
        byte? windowId,
        int slot,
        NetworkItemStack oldItem,
        NetworkItemStack newItem)
    {
        writer.WriteUnsignedVarInt(checked((int)sourceType));
        writer.WriteBool(windowId.HasValue);
        if (windowId is { } window)
            writer.WriteByte(window);
        writer.WriteBool(false);
        writer.WriteUnsignedVarInt(slot);
        oldItem.WriteNetworkItemStackDescriptor(ref writer);
        newItem.WriteNetworkItemStackDescriptor(ref writer);
    }


    /// <summary>Priority.Normal queues frames; Tick flushes OutputFrames to Server.Send.</summary>
    internal static void FlushRaknet(PlayerManager players)
    {
        foreach (var p in players.Online)
            p.Session.Transport.FlushOutgoing();
    }


    internal static byte[] ConcatCaptured(IntentTestFixture fx)
    {
        var total = 0;
        foreach (var chunk in fx.Transport.Captured)
            total += chunk.Length;
        var buf = new byte[total];
        var o = 0;
        foreach (var chunk in fx.Transport.Captured)
        {
            chunk.CopyTo(buf, o);
            o += chunk.Length;
        }

        return buf;
    }


    /// <summary>
    /// Decodes every captured RakNet datagram into its FrameSet's <see cref="Frame"/>s and
    /// reassembles split (fragmented) ones — mirroring <c>RakNetSession.HandleFragment</c>'s exact
    /// index-ordered concat. A payload larger than one datagram's MTU (ADR §142's real creative
    /// catalog is: 45 items / 19 named groups, 1.6 KB encoded, well past the 1400-byte test MTU) is
    /// no longer a contiguous span across <see cref="ConcatCaptured"/>'s raw bytes — reassembling
    /// frame payloads is the only correct way to recover one logical reliable message. Each message
    /// may itself batch more than one game packet (wrapper-framed), so callers still search for a
    /// known packet's bytes as a sub-span rather than an exact match.
    /// </summary>
    internal static List<byte[]> ReassembleApplicationPackets(IntentTestFixture fx)
    {
        var messages = new List<byte[]>();
        var fragments = new Dictionary<short, SortedDictionary<int, Frame>>();

        foreach (var datagram in fx.Transport.Captured)
        {
            if (datagram.Length == 0 || (datagram[0] & 0xf0) != (byte)Zenith.Raknet.Enumerator.BitFlags.Valid)
                continue;

            var reader = new BinaryStream(datagram, 1, datagram.Length - 1);
            var frameSet = new FrameSet();
            frameSet.Decode(ref reader);
            reader.Dispose();

            foreach (var frame in frameSet.Packets)
            {
                if (!frame.IsSplit())
                {
                    messages.Add(frame.Buffer.ToArray());
                    continue;
                }

                var split = frame.SplitInfo!;
                if (!fragments.TryGetValue(split.Id, out var parts))
                    fragments[split.Id] = parts = new SortedDictionary<int, Frame>();
                parts[split.Index] = frame;

                if (parts.Count != split.Count) continue;

                var stream = new BinaryStream();
                foreach (var part in parts.Values)
                    stream.Write(part.Buffer.Span);
                messages.Add(stream.GetBufferDisposing().ToArray());
                fragments.Remove(split.Id);
            }
        }

        return messages;
    }
}
