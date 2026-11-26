namespace ClaimsAi.Core;

public enum ProductLine
{
    Auto,
    Home,
}

/// <summary>One numbered clause of a policy document, e.g. AUTO-COMP-2026 §2.5 "Mechanical failure...".</summary>
public sealed record PolicyClause(string PolicyId, string Number, string Section, string Text)
{
    /// <summary>Stable id used for citations: "AUTO-COMP-2026#2.5".</summary>
    public string Id => $"{PolicyId}#{Number}";

    /// <summary>Policy ids start with their product line (AUTO-..., HOME-...).</summary>
    public ProductLine? ProductLine => ProductLines.FromPrefix(PolicyId);
}

/// <summary>Searches policy clauses relevant to a claim. Implemented in Infrastructure.</summary>
public interface IPolicyRetriever
{
    /// <param name="productLine">Only search policies of this line; null searches all.</param>
    IReadOnlyList<PolicyClause> Search(string query, ProductLine? productLine, int top);
}

public static class ProductLines
{
    public static ProductLine? FromPrefix(string? value) =>
        value is null ? null
        : value.StartsWith("AUTO", StringComparison.OrdinalIgnoreCase) ? Core.ProductLine.Auto
        : value.StartsWith("HOME", StringComparison.OrdinalIgnoreCase) ? Core.ProductLine.Home
        : null;

    /// <summary>The policy number decides the line; if it is missing, the claim type does ("auto_theft" -> Auto).</summary>
    public static ProductLine? For(ClaimExtraction claim) =>
        FromPrefix(claim.PolicyNumber) ?? FromPrefix(claim.ClaimType.ToString());
}
