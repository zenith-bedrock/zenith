using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace Zenith.BedrockData.Tests;

public sealed class BdsPackTests
{
    [Fact]
    public void ReadBrArchive_ReturnsEveryNamedBlob()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.brarchive");
        try
        {
            File.WriteAllBytes(path, Build(("a.json", "{\"x\":1}"), ("dir/b.json", "[]")));
            var files = BdsPack.ReadBrArchive(path);
            Assert.Equal(2, files.Count);
            Assert.Equal("{\"x\":1}", Encoding.UTF8.GetString(files["a.json"]));
            Assert.Equal("[]", Encoding.UTF8.GetString(files["dir/b.json"]));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadBrArchive_RejectsAFileWithoutTheMagic()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.brarchive");
        try
        {
            File.WriteAllBytes(path, new byte[64]);
            Assert.Throws<InvalidDataException>(() => BdsPack.ReadBrArchive(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadCategory_LaterPackReplacesEarlierFileOfTheSameName()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var (pack, body) in new[] { ("vanilla", "old"), ("vanilla_1.9", "mid"), ("vanilla_1.10", "new") })
            {
                var dir = Path.Combine(root, "behavior_packs", pack, "__brarchive");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "recipes.brarchive"), Build(("r.json", body)));
            }
            Directory.CreateDirectory(Path.Combine(root, "behavior_packs", "vanilla_education")); // not base data

            Assert.Equal(["vanilla", "vanilla_1.9", "vanilla_1.10"], BdsPack.PackOrder(root)); // numeric, not lexical
            var (pack2, data) = BdsPack.LoadCategory(root, "recipes")["r.json"];
            Assert.Equal("vanilla_1.10", pack2);
            Assert.Equal("new", Encoding.UTF8.GetString(data));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static byte[] Build(params (string Name, string Body)[] files)
    {
        var table = new byte[16 + files.Length * 256];
        Buffer.BlockCopy(new byte[] { 0x7d, 0x27, 0x25, 0xb1, 0xa0, 0x52, 0x70, 0x26 }, 0, table, 0, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(8), (uint)files.Length);
        using var blobs = new MemoryStream();
        for (var i = 0; i < files.Length; i++)
        {
            var name = Encoding.UTF8.GetBytes(files[i].Name);
            var body = Encoding.UTF8.GetBytes(files[i].Body);
            var rec = table.AsSpan(16 + i * 256, 256);
            rec[0] = (byte)name.Length;
            name.CopyTo(rec[1..]);
            BinaryPrimitives.WriteUInt32LittleEndian(rec[^8..], (uint)blobs.Position);
            BinaryPrimitives.WriteUInt32LittleEndian(rec[^4..], (uint)body.Length);
            blobs.Write(body);
        }
        return [.. table, .. blobs.ToArray()];
    }
}
