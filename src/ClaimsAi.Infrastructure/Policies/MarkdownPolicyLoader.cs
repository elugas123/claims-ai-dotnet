using System.Text.RegularExpressions;
using ClaimsAi.Core;

namespace ClaimsAi.Infrastructure.Policies;

/// <summary>
/// Splits policy documents written in Markdown into clauses:
/// <c># Title (POLICY-ID)</c>, <c>## 1. Section</c>, and clause lines starting with <c>1.1</c>.
/// </summary>
public static partial class MarkdownPolicyLoader
{
    public static IReadOnlyList<PolicyClause> LoadDirectory(string directory) =>
        Directory.EnumerateFiles(directory, "*.md")
            .Order(StringComparer.Ordinal)
            .SelectMany(path => Parse(File.ReadAllText(path)))
            .ToList();

    public static IReadOnlyList<PolicyClause> Parse(string markdown)
    {
        var clauses = new List<PolicyClause>();
        string? policyId = null;
        var section = "";
        string? number = null;
        var text = new List<string>();

        void Flush()
        {
            if (policyId is not null && number is not null)
            {
                clauses.Add(new PolicyClause(policyId, number, section, string.Join(' ', text)));
            }
            number = null;
            text.Clear();
        }

        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                Flush();
                policyId = PolicyIdPattern().Match(line) is { Success: true } m ? m.Groups[1].Value
                    : throw new FormatException($"Policy title has no (POLICY-ID): {line}");
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                section = SectionPattern().Replace(line, "");
            }
            else if (ClausePattern().Match(line) is { Success: true } clause)
            {
                Flush();
                number = clause.Groups[1].Value;
                text.Add(StripMarkdown(clause.Groups[2].Value));
            }
            else if (line.Length == 0 || line.StartsWith('>'))
            {
                Flush();
            }
            else if (number is not null)
            {
                text.Add(StripMarkdown(line));
            }
        }

        Flush();
        return clauses;
    }

    private static string StripMarkdown(string text) => text.Replace("**", "").Replace("*", "");

    [GeneratedRegex(@"\(([A-Z]+-[A-Z0-9-]+)\)\s*$")]
    private static partial Regex PolicyIdPattern();

    [GeneratedRegex(@"^##\s+(\d+\.\s*)?")]
    private static partial Regex SectionPattern();

    [GeneratedRegex(@"^(\d+\.\d+)\s+(.+)$")]
    private static partial Regex ClausePattern();
}
