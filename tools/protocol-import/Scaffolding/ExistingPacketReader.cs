using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Zenith.ProtocolImport.Scaffolding;

/// <summary>
/// Reads the wire-attributed property list off an already-migrated [GamePacket] class via a
/// plain syntax parse (no semantic compile needed) - used by `protocol-import diff` to compare
/// against a freshly pulled schema.
/// </summary>
internal static class ExistingPacketReader
{
    private static readonly string[] WireAttributeNames =
    [
        "Wire", "WireVar", "WireString", "WireByteArray", "WireUuid", "WireUuidArray", "WireNested", "WireNestedArray"
    ];

    public static IReadOnlyList<string> ReadWirePropertyNames(string filePath)
    {
        var text = File.ReadAllText(filePath);
        var tree = CSharpSyntaxTree.ParseText(text);
        var root = tree.GetCompilationUnitRoot();

        var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(a => AttributeNameIs(a, "GamePacket")));

        if (classDecl is null) return [];

        var names = new List<string>();
        foreach (var prop in classDecl.Members.OfType<PropertyDeclarationSyntax>())
        {
            var hasWireAttr = prop.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(a => WireAttributeNames.Any(w => AttributeNameIs(a, w)));

            if (hasWireAttr)
                names.Add(prop.Identifier.Text);
        }

        return names;
    }

    private static bool AttributeNameIs(AttributeSyntax attribute, string shortName)
    {
        var name = attribute.Name.ToString();
        var lastSegment = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
        return lastSegment == shortName || lastSegment == shortName + "Attribute";
    }
}
