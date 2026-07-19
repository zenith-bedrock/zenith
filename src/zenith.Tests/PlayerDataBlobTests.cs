using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class PlayerDataBlobTests
{
    public PlayerDataBlobTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Pack_unpack_round_trip()
    {
        var blob = PlayerDataBlob.Pack(3.5f, -60f, -8.25f, 90f, -15f, GameMode.Creative);
        Assert.True(PlayerDataBlob.TryUnpack(blob, out var x, out var y, out var z, out var yaw, out var pitch, out var mode));
        Assert.Equal(3.5f, x);
        Assert.Equal(-60f, y);
        Assert.Equal(-8.25f, z);
        Assert.Equal(90f, yaw);
        Assert.Equal(-15f, pitch);
        Assert.Equal(GameMode.Creative, mode);
    }

    [Fact]
    public void TryUnpack_rejects_short_and_bad_version()
    {
        Assert.False(PlayerDataBlob.TryUnpack([], out _, out _, out _, out _, out _, out _));
        var bad = PlayerDataBlob.Pack(0, -60, 0, 0, 0, GameMode.Survival);
        bad[0] = 99;
        Assert.False(PlayerDataBlob.TryUnpack(bad, out _, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void TryUnpack_rejects_oob_and_nan()
    {
        var oob = PlayerDataBlob.Pack(0, 400f, 0, 0, 0, GameMode.Survival);
        // Pack always writes; TryUnpack validates.
        Assert.False(PlayerDataBlob.TryUnpack(oob, out _, out _, out _, out _, out _, out _));

        var nan = PlayerDataBlob.Pack(float.NaN, -60f, 0, 0, 0, GameMode.Survival);
        Assert.False(PlayerDataBlob.TryUnpack(nan, out _, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void TryUnpack_rejects_unknown_gamemode_byte()
    {
        var blob = PlayerDataBlob.Pack(0, -60, 0, 0, 0, GameMode.Survival);
        blob[21] = 9;
        Assert.False(PlayerDataBlob.TryUnpack(blob, out _, out _, out _, out _, out _, out _));
    }

    [Fact]
    public async Task World_InMemory_persist_and_load()
    {
        var storage = new InMemoryChunkStorage();
        var world = new World.World(storage);
        var uuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var blob = PlayerDataBlob.Pack(12f, -60f, 4f, 45f, 0f, GameMode.Creative);
        await storage.PutPlayerDataAsync(uuid, blob);

        Assert.True(world.TryLoadPlayerData(uuid, out var x, out var y, out var z, out var yaw, out var pitch, out var mode));
        Assert.Equal(12f, x);
        Assert.Equal(-60f, y);
        Assert.Equal(4f, z);
        Assert.Equal(45f, yaw);
        Assert.Equal(0f, pitch);
        Assert.Equal(GameMode.Creative, mode);
    }

    [Fact]
    public void World_TryLoadPlayerData_miss()
    {
        var world = new World.World(new InMemoryChunkStorage());
        Assert.False(world.TryLoadPlayerData(Guid.NewGuid(), out _, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void PersistInventory_skips_unstable_identity()
    {
        var storage = new InMemoryChunkStorage();
        var world = new World.World(storage);
        var uuid = Guid.NewGuid();
        var player = new Player.Player("ephemeral", null!, 1, uuid, GameMode.Survival, identityStable: false);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 3));

        world.PersistInventory(player);
        Assert.False(world.TryLoadInventory(uuid, new PlayerInventory(seedStarterHotbar: false)));
    }

    [Fact]
    public void PersistPlayerData_skips_unstable_identity()
    {
        var storage = new InMemoryChunkStorage();
        var world = new World.World(storage);
        var uuid = Guid.NewGuid();
        var player = new Player.Player("ephemeral", null!, 1, uuid, GameMode.Creative, identityStable: false)
        {
            PositionX = 9,
            PositionY = -60,
            PositionZ = 9,
        };

        world.PersistPlayerData(player);
        Assert.False(world.TryLoadPlayerData(uuid, out _, out _, out _, out _, out _, out _));
    }
}
