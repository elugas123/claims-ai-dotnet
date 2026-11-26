namespace ClaimsAi.Core;

/// <summary>Everything the system concluded about one claim.</summary>
public sealed record ClaimAssessment(
    ExtractionResult Extraction,
    IReadOnlyList<PolicyClause> RetrievedClauses,
    CoverageResult Coverage);

/// <summary>
/// The claim pipeline: extract structured data, retrieve the relevant policy clauses (RAG), decide coverage.
/// </summary>
public sealed class ClaimAssessmentService(
    IClaimExtractor extractor,
    IPolicyRetriever retriever,
    ICoverageChecker coverageChecker)
{
    /// <summary>Tuned with the retrieval eval: 6 missed deductible and exclusion clauses, 8 finds them.</summary>
    public const int ClausesToRetrieve = 8;

    public async Task<ClaimAssessment> AssessAsync(ClaimSubmission submission, CancellationToken cancellationToken = default)
    {
        var extraction = await extractor.ExtractAsync(submission, cancellationToken);
        var clauses = RetrieveClauses(retriever, submission, extraction.Extraction);
        var coverage = await coverageChecker.CheckAsync(submission, extraction.Extraction, clauses, cancellationToken);
        return new ClaimAssessment(extraction, clauses, coverage);
    }

    public static IReadOnlyList<PolicyClause> RetrieveClauses(
        IPolicyRetriever retriever, ClaimSubmission submission, ClaimExtraction claim) =>
        retriever.Search(BuildQuery(submission, claim), ProductLines.For(claim), ClausesToRetrieve);

    /// <summary>
    /// Search text: the claim type in words, facts from the extraction spelled the way policies word them,
    /// the English summary and the customer's own text. Claims that fit no covered event ("other") are most
    /// likely excluded, so they search the exclusions.
    /// </summary>
    public static string BuildQuery(ClaimSubmission submission, ClaimExtraction claim)
    {
        var terms = new List<string>
        {
            claim.ClaimType == ClaimType.Other ? "exclusions" : string.Join(' ', SplitWords(claim.ClaimType.ToString())),
        };
        if (claim.InjuriesReported)
        {
            terms.Add("bodily injury");
        }
        if (claim.ThirdPartyInvolved)
        {
            terms.Add("third parties liability");
        }

        terms.Add(claim.Summary);
        terms.Add(submission.Text);
        return string.Join(' ', terms.Where(t => t.Length > 0));
    }

    /// <summary>"HomeWaterDamage" -> ["Home", "Water", "Damage"].</summary>
    private static IEnumerable<string> SplitWords(string pascalCase)
    {
        var start = 0;
        for (var i = 1; i <= pascalCase.Length; i++)
        {
            if (i == pascalCase.Length || char.IsUpper(pascalCase[i]))
            {
                yield return pascalCase[start..i];
                start = i;
            }
        }
    }
}
