using Spectre.Console;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Commands;

/// <summary>Shared "read a cached packet or print a red error" flow duplicated between
/// ScaffoldCommand and DiffCommand - same try/catch-and-report plus same not-cached message.</summary>
internal static class SchemaLookupHelper
{
    public static PacketSchema? TryReadPacket(ISchemaSource source, string cacheDir, string packetName, string sourceName)
    {
        PacketSchema? packet;
        try
        {
            packet = source.ReadPacket(cacheDir, packetName);
        }
        catch (Exception ex)
        {
            // The importer is explicitly a best-effort tool over hundreds of hand-varying
            // schema files (see ADR §76) - an unrecognized shape should surface as a clear
            // error, not a raw stack trace, so the human reviewing knows to just do this one
            // packet by hand instead of the tool silently producing garbage.
            AnsiConsole.MarkupLine($"[red]Failed to read '{packetName}' from source '{sourceName}': {ex.Message.EscapeMarkup()}[/]");
            return null;
        }

        if (packet is null)
        {
            AnsiConsole.MarkupLine($"[red]No cached packet named '{packetName}' for source '{sourceName}'.[/] Run `protocol-import pull --source {sourceName}` first.");
        }

        return packet;
    }
}
