using System.Security.Cryptography;
using Xunit;
using Zenith.NetherNet.Axolotl.Identity;

namespace Zenith.NetherNet.Tests.Axolotl;

public class AxolotlServerIdentityTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), "zenith-id-" + Guid.NewGuid().ToString("N"), "key.pk8");

    [Fact]
    public void LoadOrCreate_persists_a_new_key_and_reloads_the_same_identity()
    {
        var path = TempPath();
        try
        {
            var first = ServerIdentity.LoadOrCreate(path);
            var second = ServerIdentity.LoadOrCreate(path);
            Assert.Equal(first.PublicKey.ToString(), second.PublicKey.ToString());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void LoadOrCreate_reads_the_raw_pkcs8_file_written_by_the_previous_host()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            using var legacy = ECDsa.Create(ECCurve.NamedCurves.nistP384);
            File.WriteAllBytes(path, legacy.ExportPkcs8PrivateKey());

            var identity = ServerIdentity.LoadOrCreate(path);
            Assert.Equal(legacy.ExportSubjectPublicKeyInfo(), identity.PrivateKey.ExportSubjectPublicKeyInfo());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void LoadOrCreate_reads_pem_and_rejects_garbage()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var generated = ServerIdentity.Generate();
            File.WriteAllText(path, generated.ExportPrivateKeyPem());
            Assert.Equal(generated.PublicKey.ToString(), ServerIdentity.LoadOrCreate(path).PublicKey.ToString());

            File.WriteAllBytes(path, [1, 2, 3]);
            Assert.Throws<IdentityException>(() => ServerIdentity.LoadOrCreate(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }
}
