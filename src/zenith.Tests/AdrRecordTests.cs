using System.Text.RegularExpressions;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// The decision log is a convention, so it is checked like code: every `ADR §N` cited in source resolves to a record, each record
/// carries its own number, and the index lists every file. Reads the repository's files, so it is skipped when run from a
/// packaged copy without the docs.
/// </summary>
public partial class AdrRecordTests
{
    private static readonly string? RepoRoot = FindRepoRoot();

    [GeneratedRegex(@"ADR §(\d+[a-z]?)")]
    private static partial Regex Citation();

    [Fact]
    public void Every_ADR_cited_in_source_has_a_record()
    {
        if (RepoRoot is null) return;
        var adrDir = Path.Combine(RepoRoot, "docs", "adr");
        var missing = new SortedSet<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;
            foreach (Match m in Citation().Matches(File.ReadAllText(file)))
            {
                if (Directory.GetFiles(adrDir, PrefixFor(m.Groups[1].Value) + "-*.md").Length == 0)
                    missing.Add($"§{m.Groups[1].Value} (cited in {Path.GetFileName(file)})");
            }
        }

        Assert.True(missing.Count == 0, "ADR citations with no record in docs/adr:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Every_record_names_its_own_number_and_is_listed_in_the_index()
    {
        if (RepoRoot is null) return;
        var adrDir = Path.Combine(RepoRoot, "docs", "adr");
        var index = File.ReadAllText(Path.Combine(adrDir, "README.md"));
        var problems = new List<string>();
        var records = Directory.GetFiles(adrDir, "0*.md");
        Assert.NotEmpty(records);

        foreach (var file in records)
        {
            var name = Path.GetFileName(file);
            var number = Regex.Match(name, @"^0*(\d+[a-z]?)-").Groups[1].Value;
            var firstLine = File.ReadLines(file).First();
            if (!firstLine.StartsWith($"# ADR §{number} ", StringComparison.Ordinal))
                problems.Add($"{name}: first line is '{firstLine}', expected it to start with '# ADR §{number} '");
            if (!index.Contains($"]({name})", StringComparison.Ordinal))
                problems.Add($"{name}: not linked from docs/adr/README.md");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static string PrefixFor(string number)
    {
        var digits = new string(number.TakeWhile(char.IsDigit).ToArray());
        return digits.PadLeft(4, '0') + number[digits.Length..];
    }

    private static string? FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "zenith.sln")) && Directory.Exists(Path.Combine(dir.FullName, "docs", "adr")))
                return dir.FullName;
        }
        return null;
    }
}
