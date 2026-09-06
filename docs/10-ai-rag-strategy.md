# 10 — AI / RAG Strategy (Phase 7)

This document defines the Phase 7 AI/RAG plan. **AI work does not start until all Phase 7 pre-conditions are met.** See `docs/agent-guides/080-ai-rag-future-phase.md` for the agent-enforcement version.

## Why AI Is Phase 7, Not Earlier

- Phase 1–6 build the **structured data** AI needs to be useful (audit, diagnostics, findings).
- Without that data, AI on this domain hallucinates.
- AI without the data foundation is a demo, not a tool.
- Stakeholder trust must be earned with deterministic phases first.

## Hard Pre-Conditions

AI implementation begins only when **all** of these are true:

1. Phase 1–6 are stable in production (at least 90 days).
2. At least 6 months of structured audit + diagnostic data accumulated.
3. Bilgi Güvenliği has approved the AI data flow and masking strategy.
4. Siber Güvenlik has approved the AI data flow and infrastructure boundary.
5. Budget approved for: GPU server, storage, and dedicated test environment.
6. The data masking pipeline is implemented and tested with sample data.
7. This document is updated with concrete model, hardware, and rollout decisions; ADR-0005 is amended.

Any missing pre-condition blocks Phase 7 start.

## Use Cases (Initial Scope)

The Phase 7 PoC targets four narrow use cases:

1. **Past-incident summarization.** "Summarize what happened on APPSRV-12 last Tuesday between 14:00 and 16:00." (read audit + diagnostics, produce a structured summary).
2. **Recurring-pattern highlighting.** "Which servers had repeated disk alerts in the last 30 days, and what was the typical resolution?"
3. **Shift report drafting.** Compose a first-draft shift report from the structured audit data, for human edit.
4. **Runbook-aware suggestion.** Given an alert, retrieve the relevant runbook section and suggest steps the operator should consider.

**Out of scope for Phase 7 PoC:**
- Autonomous diagnostics (LLM calling tools).
- Decision making (LLM does not say "do X").
- Code generation for production scripts.
- Direct interaction with target servers.

## Non-Negotiable Constraints

### Self-Hosted Only

| Allowed | Forbidden |
|---|---|
| Local LLM via Ollama or similar | OpenAI API |
| Self-hosted Qdrant or pgvector | Anthropic API |
| On-premise Semantic Kernel orchestration | Google Gemini API |
| Local embedding models | Cohere API |
|  | Any cloud LLM endpoint |

### Data Masking Mandatory

No production data reaches the LLM without masking. See `docs/05-security-model.md` for the masking table.

### Suggest, Not Act

The LLM produces suggestions for humans. It does not:
- Trigger diagnostics
- Send notifications
- Modify state
- Make decisions that bypass the approval workflow

### Audit Everything

Every prompt and response is logged in `audit.AiAuditLog`. Same retention as operational audit.

### RAG Grounding

The LLM answers only from retrieved context. If retrieval is empty or weak, the response is "I don't have enough information."

## Architecture (Target)

```
┌─────────────────────────────────────────────┐
│ Blazor UI                                    │
│  - AI Assistant page                         │
└──────────────┬──────────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────────┐
│ ASP.NET Core API                             │
│  - /api/v1/ai/query                          │
│  - /api/v1/ai/summarize                      │
└──────────────┬──────────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────────┐
│ AI Service (SecureOps.Infrastructure.Ai)     │
│                                              │
│  Request → DataMasker → Retriever → Prompt  │
│           Builder → LLM Client → Response   │
│           Validator → AuditWriter           │
└──────┬─────────────────┬─────────────────────┘
       ▼                 ▼
  ┌─────────┐       ┌──────────┐
  │ Qdrant  │       │ Ollama   │
  │ (vector)│       │ (LLM)    │
  └─────────┘       └──────────┘
       │                 │
       │                 │
       ▼                 ▼
  ┌─────────────────────────────┐
  │ Isolated network subnet      │
  │ No outbound to public        │
  └─────────────────────────────┘
```

## Component Detail

### Data Masker

```csharp
public interface IDataMasker
{
    MaskedDocument Mask(string text);
    string Unmask(string maskedText, MaskingContext context);  // for reference resolution
}

public sealed record MaskedDocument(string Text, MaskingContext Context);
```

Replacement strategy:
- Hostnames → consistent hash within session
- Usernames → role label
- IPs → subnet
- Paths → sanitized
- Customer IDs → tokenized

Implementation in `SecureOps.Infrastructure.Ai.Masking`.

### Retriever

Queries the vector store with the user's question (also masked).

```csharp
public interface IRetriever
{
    Task<IReadOnlyList<RetrievalHit>> RetrieveAsync(
        string maskedQuery,
        int topK,
        CancellationToken ct);
}

public sealed record RetrievalHit(
    string DocumentId,
    string Content,
    double Score,
    Dictionary<string, string> Metadata);
```

Vector DB: Qdrant in primary plan. pgvector if Qdrant operationally heavy.

### Embedding Pipeline

Index sources:
- `audit.AuditLog` entries (post-masking)
- `dbo.DiagnosticResults` summaries (post-masking)
- Runbook documents (`docs/runbooks/*.md`, masking if any sensitive content)
- Past incident summaries

Schedule: weekly re-embedding job (Hangfire recurring).

Embedding model: same family as LLM, run locally.

### Prompt Builder

Construct prompts with system instruction, retrieved context, and the user's masked question.

System prompt template:

```
You are an operations assistant for a Windows server environment.
You answer ONLY based on the context provided below.
If the context is insufficient, reply: "I don't have enough information in the provided records."
You do not propose write operations. You do not execute actions.
You output structured answers with clear references to the context items by ID.
```

### LLM Client

HTTP client to local Ollama (or equivalent). No streaming in MVP; full response on completion.

Configurable model name and parameters in `appsettings.json`:

```json
{
  "Ai": {
    "Provider": "Ollama",
    "Endpoint": "http://localhost:11434",
    "Model": "llama3.1:8b-instruct",
    "Temperature": 0.2,
    "MaxTokens": 800,
    "TimeoutSeconds": 60
  }
}
```

### Response Validator

Sanity checks on LLM output:
- Length within bounds
- No references to context items that don't exist
- No hallucinated server names (cross-check against context)
- No suggestion of forbidden actions ("you should stop the service", etc.)

Failed validation → fallback response indicating an error.

### AI Audit Writer

Inserts into `audit.AiAuditLog` with masked prompt, response, model, tokens, related alert ID.

## Model Choice (To Be Finalized in ADR Update)

Candidates:

| Model | Strengths | Concerns |
|---|---|---|
| Llama 3.1 8B-instruct | Small, fast, broad reasoning | Mid-tier quality |
| Qwen 2.5 14B-instruct | Strong reasoning, good Turkish | Larger memory footprint |
| Mistral Small / Medium | Solid baseline | Mid-tier |

Decision criteria:
- Fits the GPU budget (1× A4000 or A6000 class).
- Acceptable Turkish quality (test on representative shift reports).
- Permissive license.
- Active community / maintenance.

The final choice is recorded in ADR-0005 update before implementation.

## Infrastructure Sizing (Approximate)

| Component | Sizing |
|---|---|
| GPU server | 1× workstation-class GPU (16–24 GB VRAM); 64 GB RAM; 1 TB SSD |
| Vector DB | Co-locate on same server or adjacent VM; 100 GB initial |
| Network | Isolated subnet, no outbound internet |
| OS | Windows Server or Linux (Ubuntu LTS for Ollama-native) |

## Rollout Plan

1. **Procurement and provisioning** (2–4 weeks).
2. **Masking pipeline implementation and testing** (2 weeks).
3. **Embedding pipeline + initial corpus** (2 weeks).
4. **Retrieval + generation prototype** (2 weeks).
5. **PoC with 3 use cases** (2 weeks).
6. **Operator feedback session** (1 week).
7. **Go / no-go for broader rollout**.

Total: 11–13 weeks, plus procurement lead time.

## Success Criteria for Phase 7 PoC

- All AI interactions audited.
- Zero unmasked data reaches the LLM.
- 3 representative scenarios produce useful outputs in operator review.
- Response time < 10 seconds for typical queries.
- Operators report the assistant is helpful in a structured survey.

## Failure Modes and Responses

| Failure | Response |
|---|---|
| LLM hallucination | Validator catches; show error message |
| Retrieval empty | Show "no relevant records" message |
| Masking gap detected | Block prompt; alert engineer; audit failure |
| Operator misuse (treating AI as authoritative) | Operator guidance; UI labels |
| Model performance regression | Roll back model version |

## After PoC

Possible Phase 7+ extensions (each its own ADR):
- Fine-tuning on masked data
- Additional use cases
- Operator-facing chat UI
- Conversation history (still audited)

## Reference

- `docs/agent-guides/080-ai-rag-future-phase.md`
- `docs/05-security-model.md` (masking)
- `docs/08-audit-model.md` (AI audit table)
- `docs/adr/ADR-0005-ai-rag-later-phase.md`
