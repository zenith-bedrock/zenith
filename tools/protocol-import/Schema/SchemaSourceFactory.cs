namespace Zenith.ProtocolImport.Schema;

internal static class SchemaSourceFactory
{
    public const string Endstone = "endstone";
    public const string Mojang = "mojang";

    public static ISchemaSource Create(string name, string? localRepository = null) => name.Trim().ToLowerInvariant() switch
    {
        Endstone => new EndstoneSchemaSource(localRepository),
        Mojang => new MojangSchemaSource(localRepository),
        _ => throw new ArgumentException($"Unknown source '{name}'. Valid: {Endstone}, {Mojang}.")
    };
}
