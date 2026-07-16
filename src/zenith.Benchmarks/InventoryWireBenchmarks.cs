using BenchmarkDotNet.Attributes;
using Zenith.Packets;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Benchmarks;

/// <summary>InventorySystem egress: full bag content + ISR OK/Error responses.</summary>
[MemoryDiagnoser]
public class InventoryWireBenchmarks
{
    private InventoryContentPacket _content = null!;
    private ItemStackResponsePacket _ok = null!;
    private ItemStackResponsePacket _error = null!;

    [GlobalSetup]
    public void Setup()
    {
        Blocks.EnsureLoaded();
        var palette = ItemPaletteLoader.FromEmbeddedResource();
        var stoneNet = palette.Require("minecraft:stone");
        var dirtNet = palette.Require("minecraft:dirt");

        var slots = new NetworkItemStack[PlayerInventory.FullInventorySize];
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = i < 9
                ? new NetworkItemStack(stoneNet, (ushort)(i + 1), Blocks.Stone, StackNetworkId: i + 1)
                : i < 18
                    ? new NetworkItemStack(dirtNet, 16, Blocks.Dirt, StackNetworkId: i + 1)
                    : NetworkItemStack.Empty;
        }

        _content = new InventoryContentPacket
        {
            WindowId = InventoryContentPacket.WindowInventory,
            Slots = slots
        };

        _ok = ItemStackResponsePacket.Ok(42,
        [
            new StackResponseContainerInfo
            {
                Container = new FullContainerName { ContainerId = 12 },
                SlotInfo =
                [
                    new StackResponseSlotInfo
                    {
                        Slot = 0,
                        HotbarSlot = 0,
                        Count = 3,
                        StackNetworkId = 1
                    },
                    new StackResponseSlotInfo
                    {
                        Slot = 9,
                        HotbarSlot = 9,
                        Count = 1,
                        StackNetworkId = 2
                    }
                ]
            },
            new StackResponseContainerInfo
            {
                Container = new FullContainerName { ContainerId = 28 },
                SlotInfo =
                [
                    new StackResponseSlotInfo
                    {
                        Slot = 0,
                        HotbarSlot = 0,
                        Count = 1,
                        StackNetworkId = 3
                    }
                ]
            }
        ]);

        _error = ItemStackResponsePacket.Error(43);
    }

    [Benchmark]
    public int EncodeInventoryContent36() => _content.Encode().Length;

    [Benchmark]
    public int EncodeItemStackResponseOk() => _ok.Encode().Length;

    [Benchmark]
    public int EncodeItemStackResponseError() => _error.Encode().Length;
}
