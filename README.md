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

## Roadmap

- [ ] Month 1 – extraction + RAG over policies
- [ ] Month 2 – agent + evals
- [ ] Month 3 – public deploy + demo video
