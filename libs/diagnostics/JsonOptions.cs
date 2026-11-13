using System.Text.Json;

namespace Zenith.Diagnostics;

/// <summary>Shared serializer options: a <see cref="JsonSerializerOptions"/> is expensive to build and is meant to be reused.</summary>
internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    public static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };
}
