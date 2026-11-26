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
- xUnit for unit tests and evals
- Vector store for RAG (planned)

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
  ClaimsAi.Core            ClaimExtraction, IClaimExtractor, JSON contract
  ClaimsAi.Infrastructure  ClaudeClaimExtractor
  ClaimsAi.Api             HTTP endpoints
tests/
  ClaimsAi.UnitTests       fast offline tests of Core
  ClaimsAi.Evals           scorer + eval runner; live eval against Claude
data/
  policies                 synthetic policy documents (Contoso Insurance)
  claims                   labeled eval set: claim text + expected extraction
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

## Roadmap

- [x] Extraction with structured outputs + labeled eval set
- [x] Eval runner: per-field accuracy, cost, latency, saved history
- [ ] First real eval run and prompt iteration
- [ ] Month 1 – RAG over policies
- [ ] Month 2 – agent + evals
- [ ] Month 3 – public deploy + demo video
