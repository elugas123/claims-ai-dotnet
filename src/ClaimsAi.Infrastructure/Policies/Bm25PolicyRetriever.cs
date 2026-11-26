using System.Text.RegularExpressions;
using ClaimsAi.Core;

namespace ClaimsAi.Infrastructure.Policies;

/// <summary>
/// Keyword search over policy clauses with BM25, the ranking function classic search engines use.
/// In-memory and dependency-free: enough for a few hundred clauses, and easy to swap for embeddings
/// behind <see cref="IPolicyRetriever"/> if the evals show keyword search is missing relevant clauses.
/// </summary>
public sealed partial class Bm25PolicyRetriever : IPolicyRetriever
{
    private const double K1 = 1.2;
    private const double B = 0.75;

    private readonly IReadOnlyList<PolicyClause> _clauses;
    private readonly List<Dictionary<string, int>> _termCounts;
    private readonly List<int> _lengths;
    private readonly Dictionary<string, int> _documentFrequency = [];
    private readonly double _averageLength;

    public Bm25PolicyRetriever(IReadOnlyList<PolicyClause> clauses)
    {
        _clauses = clauses;
        _termCounts = [];
        _lengths = [];

        foreach (var clause in clauses)
        {
            // The section name ("Exclusions", "Deductibles") is indexed too: it tells what kind of clause this is.
            var tokens = Tokenize($"{clause.Section} {clause.Text}");
            var counts = tokens.CountBy(t => t).ToDictionary(p => p.Key, p => p.Value);
            _termCounts.Add(counts);
            _lengths.Add(tokens.Count);
            foreach (var term in counts.Keys)
            {
                _documentFrequency[term] = _documentFrequency.GetValueOrDefault(term) + 1;
            }
        }

        _averageLength = _lengths.Count == 0 ? 0 : _lengths.Average();
    }

    public IReadOnlyList<PolicyClause> Search(string query, ProductLine? productLine, int top)
    {
        var queryTerms = Tokenize(query).Distinct().ToList();

        return Enumerable.Range(0, _clauses.Count)
            .Where(i => productLine is null || _clauses[i].ProductLine == productLine)
            .Select(i => (Index: i, Score: Score(i, queryTerms)))
            .Where(r => r.Score > 0)
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Index)
            .Take(top)
            .Select(r => _clauses[r.Index])
            .ToList();
    }

    private double Score(int index, List<string> queryTerms)
    {
        var counts = _termCounts[index];
        var lengthNorm = 1 - B + B * _lengths[index] / _averageLength;
        var score = 0.0;

        foreach (var term in queryTerms)
        {
            if (!counts.TryGetValue(term, out var tf))
            {
                continue;
            }
            var df = _documentFrequency[term];
            var idf = Math.Log(1 + (_clauses.Count - df + 0.5) / (df + 0.5));
            score += idf * tf * (K1 + 1) / (tf + K1 * lengthNorm);
        }

        return score;
    }

    private static readonly HashSet<string> StopWords =
    [
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "has", "have", "i", "in", "is", "it",
        "its", "my", "of", "on", "or", "our", "that", "the", "their", "this", "to", "was", "we", "were", "with",
    ];

    /// <summary>Lowercase words without stop words, plurals singularized so "injuries" matches "injury".</summary>
    internal static List<string> Tokenize(string text) =>
        NonWord().Split(text.ToLowerInvariant())
            .Where(t => t.Length > 0 && !StopWords.Contains(t))
            .Select(Singular)
            .ToList();

    private static string Singular(string word) =>
        word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal) ? word[..^3] + "y"
        : word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) ? word[..^1]
        : word;

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();
}
