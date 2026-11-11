using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Zenith.NetherNet.Axolotl.Identity;

/// <summary>Strict JSON helpers for identity structures (port of <c>Json</c>).</summary>
public static class Json
{
    private static readonly JsonSerializerOptions EncodeOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static JsonObject DecodeObject(string json, string what)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException e)
        {
            throw new IdentityException($"Malformed {what}: {e.Message}", e);
        }
        if (node is not JsonObject obj)
            throw new IdentityException(char.ToUpperInvariant(what[0]) + what[1..] + " is not a JSON object");
        return obj;
    }

    /// <summary>Encodes without escaping slashes; property order is insertion order.</summary>
    public static string Encode(JsonObject value) => value.ToJsonString(EncodeOptions);
}
