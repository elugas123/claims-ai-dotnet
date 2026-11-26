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

- ASP.NET Core (.NET 10)
- `Microsoft.Extensions.AI`
- Vector store for RAG
- Simple web frontend

## Project layout

```
src/ClaimsAi.Core     domain model, Claude-based extractor, eval scorer
src/ClaimsAi.Api      ASP.NET Core minimal API
tools/ClaimsAi.Evals  eval runner (console)
tests/ClaimsAi.Tests  offline unit tests (no API key needed)
data/policies         synthetic policy documents (Contoso Insurance)
data/claims           labeled eval set: claim text + expected extraction
```

## Getting started

```bash
dotnet test                       # offline tests
cd src/ClaimsAi.Api
dotnet user-secrets set "Anthropic:ApiKey" "<your-key>"   # or set ANTHROPIC_API_KEY
dotnet run
```

```bash
curl -X POST http://localhost:5295/claims/extract \
  -H "Content-Type: application/json" \
  -d '{"text": "My car was stolen last night from Riverside Mall. Plate MNB-7733, policy AUTO-102938.", "received_at": "2026-11-05"}'
```

The extractor makes one Claude call with a JSON Schema structured output, so the response always matches `ClaimExtraction`.

## Evals

`tools/ClaimsAi.Evals` runs the extractor over the labeled eval set and reports accuracy per field, cost and latency.
Each run is saved to `evals/results/` so changes to the prompt, model or effort can be compared over time.

```bash
dotnet run --project tools/ClaimsAi.Evals                    # needs ANTHROPIC_API_KEY
dotnet run --project tools/ClaimsAi.Evals -- --effort medium --min-accuracy 0.9
dotnet run --project tools/ClaimsAi.Evals -- --self-check    # no API calls, checks the pipeline
```

```
  C010  FAIL   9/10    2.4s
          incident_date: expected null, got 2026-11-18
...
Field accuracy
  claim_type             100.0 %  12/12
  incident_date           91.7 %  11/12
...
Overall   ...% of fields · .../12 cases perfect · 0 errors
Tokens    ... in · ... out · $...
Latency   p50 ...s · p95 ...s
```

`--min-accuracy` makes the command exit with code 1 below the threshold, so it can gate a CI pipeline.

## Roadmap

- [x] Extraction with structured outputs + labeled eval set
- [x] Eval runner: per-field accuracy, cost, latency, saved history
- [ ] First real eval run and prompt iteration
- [ ] Month 1 – RAG over policies
- [ ] Month 2 – agent + evals
- [ ] Month 3 – public deploy + demo video
