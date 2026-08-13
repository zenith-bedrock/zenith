using Spectre.Console;
using Zenith.ProtocolImport.Commands;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Scaffolding;

/// <summary>
/// Applies only additive scalar changes to files wholly owned by protocol-import. Existing packet
/// sources stay human-owned: removing or rewriting their fields would silently change a wire
/// contract and is intentionally left as a reviewed reconcile action.
/// </summary>
internal static class GreenUpgradePatchApplier
{
    public static void Apply(
        IEnumerable<SchemaChange> changes, IReadOnlyList<PacketSchema> target,
        string? packetsDir, string? testsDir)
    {
        var root = RepoLocator.FindRoot(Directory.GetCurrentDirectory());
        packetsDir ??= root is null ? null : Path.Combine(root, "src", "zenith", "Packets");
        testsDir ??= root is null ? null : Path.Combine(root, "src", "zenith.Tests");
        if (packetsDir is null || testsDir is null || !Directory.Exists(packetsDir) || !Directory.Exists(testsDir))
        {
            AnsiConsole.MarkupLine("[red]--apply-green requires existing --packets-dir and --tests-dir.[/]");
            return;
        }

        var schemas = target.ToDictionary(packet => packet.Name, StringComparer.Ordinal);
        foreach (var change in changes.Where(change => change.Description.StartsWith("Added field: ", StringComparison.Ordinal)))
        {
            if (!schemas.TryGetValue(change.Packet, out var packet)) continue;
            var fieldName = change.Description["Added field: ".Length..];
            var packetPath = Path.Combine(packetsDir, packet.Name + ".cs");
            if (!File.Exists(packetPath) || !File.ReadAllText(packetPath).Contains("[GamePacket("))
            {
                AnsiConsole.MarkupLine($"[yellow]Suggest only:[/] {change.Packet.EscapeMarkup()} — no generated local packet file to extend.");
                continue;
            }

            var property = PropertyNamer.ToPascalCase(fieldName);
            if (ExistingPacketReader.ReadWirePropertyNames(packetPath).Contains(property)) continue;
            var field = packet.Fields.FirstOrDefault(candidate => candidate.Name == fieldName);
            var patch = "";
            var reason = field is null ? "target field is absent" : "";
            if (field is null || !GreenAttributePatchScaffolder.TryScaffold(packet, field, out patch, out reason))
            {
                AnsiConsole.MarkupLine($"[yellow]Suggest only:[/] {change.Packet.EscapeMarkup()}.{fieldName.EscapeMarkup()} — {reason.EscapeMarkup()}.");
                continue;
            }

            var output = Path.Combine(packetsDir, $"{packet.Name}.ProtocolUpgrade.g.cs");
            var testOutput = Path.Combine(testsDir, $"{packet.Name}ProtocolUpgradeTests.cs");
            if (File.Exists(output) || File.Exists(testOutput))
            {
                AnsiConsole.MarkupLine($"[yellow]Suggest only:[/] {packet.Name.EscapeMarkup()} — generated upgrade output already exists; never overwriting it.");
                continue;
            }

            File.WriteAllText(output, patch);
            File.WriteAllText(testOutput, BasicTest(packet.Name, property));
            AnsiConsole.MarkupLine($"[green]Generated GREEN attribute patch + basic round-trip test:[/] {packet.Name.EscapeMarkup()}.{property.EscapeMarkup()}");
        }
    }

    private static string BasicTest(string packetName, string property) => $$"""
using Xunit;
using Zenith.Packets;
using Zenith.Raknet.Stream;

namespace Zenith.Tests;

public sealed class {{packetName}}ProtocolUpgradeTests
{
    [Fact]
    public void {{packetName}}_{{property}}_default_value_decodes_after_upgrade()
    {
        var encoded = new {{packetName}}().Encode().ToArray();
        var stream = new BinaryStream(encoded);
        var decoded = new {{packetName}}();
        decoded.Decode(ref stream);
        stream.Dispose();
        Assert.Equal(default, decoded.{{property}});
    }
}
""";
}
