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
curl -X POST http://localhost:5000/claims/extract \
  -H "Content-Type: application/json" \
  -d '{"text": "My car was stolen last night from Riverside Mall. Plate MNB-7733, policy AUTO-102938.", "received_at": "2026-11-05"}'
```

The extractor makes one Claude call with a JSON Schema structured output, so the response always matches `ClaimExtraction`.

## Roadmap

- [x] Extraction with structured outputs + labeled eval set
- [ ] Month 1 – eval runner + RAG over policies
- [ ] Month 2 – agent + evals
- [ ] Month 3 – public deploy + demo video
