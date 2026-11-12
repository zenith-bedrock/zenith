using Xunit;
using Zenith.Binary;
using Zenith.Packets;

namespace Zenith.Tests;

/// <summary>
/// Item descriptors exactly as a stock BDS 1.26.63.1 sent them (<c>bds-reference/capture-damage.ts</c>, console
/// <c>give</c> commands; ADR §148). The hex is the stack inside the captured InventoryTransaction packet; the network
/// ids are that server's item registry (iron_pickaxe 299, bow 302, shield 358, bed 421).
/// </summary>
public class NetworkItemStackBdsGoldenTests
{
    private static string Encode(NetworkItemStack stack)
    {
        var writer = new BinaryStream();
        stack.WriteNetworkItemStackDescriptor(ref writer);
        return Convert.ToHexString(writer.GetBufferDisposing()).ToLowerInvariant();
    }

    [Fact]
    public void A_damaged_iron_pickaxe_matches_the_bytes_BDS_sent()
    {
        // give iron_pickaxe 1 100 -> Damage 100 in the NBT, meta (aux) 0, stack net id 4
        var stack = new NetworkItemStack(299, 1, BlockRuntimeId: 0, Meta: 0, StackNetworkId: 4, Damage: 100);

        Assert.Equal("2b0101000001080" + "01cffff010a000003060044616d616765" + "64000000" + "00" + "0000000000000000", Encode(stack));
    }

    [Fact]
    public void A_bow_with_damage_50_matches_the_bytes_BDS_sent()
    {
        var stack = new NetworkItemStack(302, 1, BlockRuntimeId: 0, Meta: 0, StackNetworkId: 8, Damage: 50);

        Assert.Equal("2e0101000001100" + "01cffff010a000003060044616d616765" + "32000000" + "00" + "0000000000000000", Encode(stack));
    }

    [Fact]
    public void A_new_shield_carries_Damage_0_and_the_blocking_tick_like_BDS()
    {
        // 36-byte extra: NBT marker + compound, can-place/can-destroy counts, then the shield's i64 blocking tick.
        var stack = new NetworkItemStack(358, 1, BlockRuntimeId: 0, Meta: 0, StackNetworkId: 6, IsShield: true, Damage: 0);

        Assert.Equal("6601010000010c00" + "24ffff010a000003060044616d6167650000000000" + "0000000000000000" + "0000000000000000", Encode(stack));
    }

    [Fact]
    public void A_variant_item_without_damage_has_meta_and_no_NBT_like_the_lime_bed()
    {
        // give bed 1 5 -> meta 5, no Damage tag (a bed does not wear), 10-byte extra
        var stack = new NetworkItemStack(421, 1, BlockRuntimeId: 0, Meta: 5, StackNetworkId: 12);

        Assert.Equal("a50101000501" + "18" + "00" + "0a" + "00000000000000000000", Encode(stack));
    }
}
