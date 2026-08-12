using System.Text.Json;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Commands;

/// <summary>Stable CI payloads; add fields compatibly rather than changing existing names/types.</summary>
internal static class ProtocolGovernanceJson
{
    public static string Compatibility(ProtocolGovernance report) => JsonSerializer.Serialize(new
    {
        protocol = report.Protocol,
        coverage = Math.Round(report.Coverage, 4),
        generated = report.GeneratedPackets,
        manual = report.ManualPackets,
        generatedManualRatio = report.ManualPackets == 0 ? report.GeneratedPackets : (double)report.GeneratedPackets / report.ManualPackets,
        red = report.Red.Count,
        yellow = report.Yellow.Count,
        redItems = report.Red.Select(item => new { packet = item.Packet, field = item.Field, reason = item.Reason }),
        yellowItems = report.Yellow.Select(item => new { packet = item.Packet, field = item.Field, reason = item.Reason }),
        generatedOutOfDate = report.GeneratedOutOfDate.Count > 0,
        validationErrors = report.ValidationErrors
    });

    public static string Validation(ProtocolGovernance report, bool valid) => JsonSerializer.Serialize(new
    {
        protocol = report.Protocol,
        coverage = Math.Round(report.Coverage, 4),
        red = report.Red.Count,
        yellow = report.Yellow.Count,
        generatedOutOfDate = report.GeneratedOutOfDate.Count > 0,
        valid
    });
}
