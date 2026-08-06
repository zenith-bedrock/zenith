using System.Text;

namespace Zenith.ProtocolImport.Scaffolding;

internal static class PropertyNamer
{
    /// <summary>"Target Actor Runtime ID" -> "TargetActorRuntimeId". Splits on anything that
    /// isn't a letter/digit and capitalizes each word. An all-caps word like "ID"/"UUID" is
    /// title-cased to "Id"/"Uuid" (matches the codebase's own naming style), but a word that's
    /// already mixed-case (e.g. Endstone's "IsInternal" declared with no separators at all) is
    /// left untouched instead of being flattened to "Isinternal" - only single all-caps words
    /// are ambiguous about intended casing, mixed-case ones already say what they mean.</summary>
    public static string ToPascalCase(string raw)
    {
        var sb = new StringBuilder();
        var words = raw.Split([' ', '_', '-', '.', ':'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            if (word.Length == 0) continue;

            var isAllUpper = word.All(c => !char.IsLower(c));
            if (isAllUpper)
            {
                sb.Append(char.ToUpperInvariant(word[0]));
                for (var i = 1; i < word.Length; i++)
                    sb.Append(char.ToLowerInvariant(word[i]));
            }
            else
            {
                sb.Append(char.ToUpperInvariant(word[0]));
                sb.Append(word, 1, word.Length - 1);
            }
        }

        var result = sb.ToString();
        return result.Length == 0 ? "Field" : result;
    }
}
