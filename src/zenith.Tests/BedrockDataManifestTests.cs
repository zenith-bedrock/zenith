using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
using Zenith.Server;
using Zenith.World;

namespace Zenith.Tests;

public class BedrockDataManifestTests
{
    [Fact]
    public void Embedded_assets_match_the_protocol_target_and_recorded_source_hashes()
    {
        var assembly = typeof(ItemPaletteLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream("bedrock_data_manifest.json");
        Assert.NotNull(stream);
        using var manifest = JsonDocument.Parse(stream);
        var root = manifest.RootElement;
        Assert.Equal(ServerIdentity.ProtocolVersion, root.GetProperty("protocol").GetInt32());
        Assert.Equal(ServerIdentity.VersionName, root.GetProperty("minecraft_version").GetString());
        foreach (var asset in root.GetProperty("assets").EnumerateObject())
        {
            using var resource = assembly.GetManifestResourceStream(asset.Name);
            Assert.NotNull(resource);
            Assert.Equal(asset.Value.GetProperty("bytes").GetInt64(), resource.Length);
            Assert.Equal(asset.Value.GetProperty("sha256").GetString(),
                Convert.ToHexStringLower(SHA256.HashData(resource)));
        }
    }
}
