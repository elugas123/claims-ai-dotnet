# claims-ai-dotnet

AI-powered insurance claims processing for a fictional insurer, built with .NET.

> Portfolio project. The company, policies and claims are all synthetic.

## What it does

1. **Intake** – receives a claim as PDF, photos or free text.
2. **Extraction** – turns it into structured data (date, location, claim type, plate, estimated amount).
3. **Coverage check (RAG)** – searches the policy documents and cites the clause that applies.
4. **Agent** – decides what information is missing, flags possible fraud signals and drafts the reply to the customer.
5. **Evals** – a test set of 50–100 labeled claims and a dashboard with accuracy per step.
6. **Cost & latency** – tracks cost and time per processed claim.

## Stack

- ASP.NET Core minimal API (.NET 10)
- Claude via the official Anthropic C# SDK, with structured outputs
- RAG over policy documents with in-memory BM25 keyword search
- xUnit for unit tests and evals

## Architecture

A lightweight [Clean Architecture](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures):
dependencies point inward, and the AI provider is an implementation detail behind an interface.

```
          ClaimsAi.Api  ──────────────┐   composition root (Program.cs)
              │                       │
              ▼                       ▼
        ClaimsAi.Core  ◄──  ClaimsAi.Infrastructure
   domain model + interfaces     Claude implementation
     (no external packages)       (Anthropic SDK)
```

Kept deliberately simple (KISS): no repositories, mediators or CQRS until a real need shows up.

```
src/
  ClaimsAi.Core            domain model, interfaces, ClaimAssessmentService (the pipeline), JSON contract
  ClaimsAi.Infrastructure  Claude extractor + coverage checker, policy loader, BM25 retriever
  ClaimsAi.Api             HTTP endpoints
tests/
  ClaimsAi.UnitTests       fast offline tests
  ClaimsAi.Evals           extraction + RAG evals (offline retrieval eval, live evals against Claude)
data/
  policies                 synthetic policy documents (Contoso Insurance)
  claims                   labeled eval set: claim text + expected extraction + expected coverage
evals/results              saved eval runs (JSON)
```

Shared build settings live in `Directory.Build.props` and package versions in `Directory.Packages.props`
(Central Package Management).

## Getting started

```bash
dotnet test                       # all offline tests; the live eval is skipped without an API key
cd src/ClaimsAi.Api
dotnet run                        # needs ANTHROPIC_API_KEY (or user-secret "Anthropic:ApiKey")
```

```bash
curl -X POST http://localhost:5295/claims/extract \
  -H "Content-Type: application/json" \
  -d '{"text": "My car was stolen last night from Riverside Mall. Plate MNB-7733, policy AUTO-102938.", "received_at": "2026-11-05"}'
```

The extractor makes one Claude call with a JSON Schema structured output, so the response always matches `ClaimExtraction`.

`POST /claims/assess` runs the full pipeline and returns the extraction, the coverage decision and the clauses it
was based on:

```json
{
  "coverage": {
    "verdict": "not_covered",
    "cited_clauses": ["AUTO-COMP-2026#2.5"],
    "deductible": null,
    "required_documents": [],
    "reasoning": "A failed transmission is a mechanical failure, which clause 2.5 excludes..."
  },
  "retrieved_clauses": [{ "id": "AUTO-COMP-2026#2.5", "section": "Exclusions", "text": "Mechanical failure, ..." }]
}
```

## Coverage check (RAG)

```
claim text ──► extract (Claude) ──► build query ──► BM25 over policy clauses ──► top 8 clauses
                                                                                    │
                                    verdict + citations + deductible  ◄── Claude ◄──┘
```

1. **Chunking** – policies are split into their numbered clauses (`AUTO-COMP-2026#2.5`), so every chunk is a
   citable unit with its section ("Exclusions", "Deductibles").
2. **Retrieval** – BM25 keyword search, filtered to the claim's product line (auto/home). The query combines the
   claim type, facts from the extraction worded like the policies ("bodily injury", "third parties"), the summary
   and the customer's text.
3. **Generation** – Claude decides `covered` / `not_covered` / `needs_review` using only those clauses, with a
   structured output. Citations to clauses it was not given are dropped.

Why BM25 and not embeddings: policy language is precise and repetitive (collision, theft, deductible), keyword
search handles it well, and it needs no extra service. With two short policies the whole document would fit in the
prompt; retrieval is here so the design scales to hundreds of policies, and it sits behind `IPolicyRetriever` so
embeddings can replace it if the retrieval eval shows misses.

## Evals

Evals run as tests, the way Microsoft's [AI evaluation docs](https://learn.microsoft.com/en-us/dotnet/ai/evaluation/libraries)
recommend, so they work with `dotnet test`, Test Explorer and CI.

`LiveExtractionEval` sends every labeled claim to Claude, scores each field, prints a report and saves it to
`evals/results/` so changes to the prompt, model or effort can be compared over time. It fails if overall accuracy
drops below 85%, and gates on the overall score rather than single cases because model output varies a little
between runs.

```bash
dotnet test --filter Category=LiveEval --logger "console;verbosity=detailed"   # needs ANTHROPIC_API_KEY
```

```
  C010  FAIL   9/10    2.4s
          incident_date: expected null, got 2026-11-18
...
Field accuracy
  claim_type              100.0%  12/12
  incident_date            91.7%  11/12
...
Overall   ...% of fields · .../12 cases perfect · 0 errors
Tokens    ... in · ... out · $...
Latency   p50 ...s · p95 ...s
```

The RAG evals are split in two so a failure points at the right half:

| Eval | Runs | Measures | Current |
|---|---|---|---|
| `Retrieval_finds_the_clauses_each_claim_needs` | offline, every `dotnet test` | recall@8 of the labeled clauses | 95.7% (22/23) |
| `Coverage_verdicts_and_citations_match_the_labels` | live (`Category=LiveEval`) | verdict accuracy, citation recall | not run yet |

The retrieval eval drove the design: top 6 missed deductible and exclusion clauses, and adding injury / third-party
terms from the extraction found the liability clause. The one remaining miss (C012, a mechanical failure) happens
because the offline eval has no model summary; in the live pipeline the summary names the failure.

## Roadmap

- [x] Extraction with structured outputs + labeled eval set
- [x] Eval runner: per-field accuracy, cost, latency, saved history
- [x] RAG over policies: clause chunking, BM25 retrieval, coverage decision with citations, retrieval eval
- [ ] First real eval runs and prompt iteration
- [ ] Month 2 – agent + evals
- [ ] Month 3 – public deploy + demo video
