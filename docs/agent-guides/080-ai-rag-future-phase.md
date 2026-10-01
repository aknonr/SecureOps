# 080 — AI / RAG Rules (Phase 7 — Future Phase)

## Applicability

- **Purpose:** AI/RAG rules for Phase 7 only. Self-hosted only; read before any AI work.
- **Applies to:** `src/SecureOps.Infrastructure/Ai/**/*.cs` and `scripts/python/ai/**/*.py`, plus any AI design or proposal.
- **Loading:** Routed explicitly from `AGENTS.md` or `docs/agent-guides/README.md`; do not assume automatic discovery.

## Hard Pre-Conditions

AI work begins **only when all of these are true**:

1. Phase 1–6 are complete and stable in production.
2. At least 6 months of structured audit + diagnostic data exists.
3. Bilgi Güvenliği AND Siber Güvenlik teams have approved the data flow.
4. A separate AI budget has been approved.
5. A self-hosted GPU server is provisioned.
6. Data masking strategy is implemented and tested.
7. An ADR documenting the AI rollout plan exists (`docs/adr/ADR-0005-ai-rag-later-phase.md` is updated with concrete plan).

If any of these is missing, AI work does not start. Surface the gap to the user.

## Hard Boundaries

### 1. Self-Hosted Only

Allowed:
- Local LLM via Ollama or equivalent self-hosted runtime
- Self-hosted vector database (Qdrant, Weaviate, pgvector)
- On-premise Semantic Kernel or LlamaIndex orchestration

Forbidden:
- OpenAI API
- Anthropic API
- Google Gemini API
- Cohere API
- Hugging Face Inference API
- Any cloud-hosted inference endpoint
- Microsoft Copilot for Security (unless explicitly ADR-approved with data lineage analysis)

### 2. AI Suggests, Does Not Act

The AI layer can produce:

- Summaries of past incidents.
- Highlight recurring patterns it noticed.
- Suggest which runbook section might apply.
- Draft text for shift reports.
- Answer "what happened last Tuesday?" style queries against indexed audit data.

The AI layer cannot:

- Trigger a diagnostic.
- Open or modify a ticket.
- Send a notification.
- Restart a service, recycle an app pool, or modify any state.
- Make decisions that bypass the approval workflow.

Every AI output is a **suggestion to a human operator**. The operator decides.

### 3. Data Masking Before Inference

Every payload sent to the local LLM must pass through the masking layer:

- Hostnames → consistent hash (`SRV-A1B2C3`)
- Usernames → role label (`<admin-user>`)
- IP addresses → subnet only (`10.0.x.x`)
- File paths with user names → masked (`C:\Users\<masked>\...`)
- Production database/service names → generic labels
- Customer-identifiable data → never sent

Masking happens at the application layer **before** any LLM call. Implement and test masking thoroughly before any inference call.

### 4. Prompt and Response Audit

Every LLM interaction is audited:

```csharp
public sealed record AiAuditEntry(
    Guid Id,
    DateTimeOffset Timestamp,
    string Actor,             // user who triggered, or "system:scheduled"
    string MaskedPrompt,      // post-masking prompt
    string Response,
    string ModelName,
    string ModelVersion,
    int PromptTokens,
    int CompletionTokens,
    Guid? RelatedAlertId);
```

Stored in `audit.AiAuditLog`, append-only, same retention as operational audit.

### 5. Retrieval-Augmented Generation (RAG) Pattern

The AI does not "know" things from training data alone. Every response is grounded in:

1. A retrieval step against the vector store.
2. The retrieved chunks are injected into the prompt.
3. The LLM is instructed to answer only from the retrieved context.
4. The response includes references to source records.

If retrieval returns nothing relevant, the AI responds "I don't have enough information." No hallucinated answers.

## Architecture (Phase 7 Target)

```
┌─────────────────────────┐
│ Web UI (Blazor)         │
└────────────┬────────────┘
             │ HTTPS
             ▼
┌─────────────────────────┐
│ API (ASP.NET Core)      │
└────────────┬────────────┘
             │ (call to AI service)
             ▼
┌─────────────────────────────────────┐
│ AI Service (SecureOps.Infrastructure.Ai) │
│  ├─ DataMasker                       │
│  ├─ Retriever (queries vector DB)    │
│  ├─ Prompt Builder                   │
│  ├─ LLM Client (Ollama HTTP)         │
│  ├─ Response Validator               │
│  └─ AuditWriter                      │
└────┬─────────────────────────┬──────┘
     ▼                          ▼
┌─────────┐               ┌──────────┐
│ Qdrant  │               │ Ollama   │
│ (vector)│               │ (LLM)    │
└─────────┘               └──────────┘
```

## Model Choice (Phase 7 Decision)

Candidates (final decision in ADR-0005 update):

- **Llama 3 / 3.1 8B-instruct** — small, fast, good general quality.
- **Qwen 2.5 14B-instruct** — strong reasoning, larger memory footprint.
- **Mistral Small / Medium** — solid baseline.

Decision criteria:
- Fits on the GPU budget (1× A4000/A6000 class).
- Supports Turkish reasonably well (test before commit).
- Permissive license for commercial use.
- Active maintenance.

## Vector Database

- **Qdrant** preferred (Rust-based, self-hosted, scalable).
- Alternative: `pgvector` extension if the team prefers SQL-adjacent.
- Index documents from `AuditLog`, `DiagnosticResults`, runbooks, and incident summaries.
- Embedding model: same family as LLM, run locally.

## Embedding Pipeline

```
Source data (SQL) → Mask → Chunk → Embed → Store in Qdrant
                                          │
                                          ▼
                              Audit: which records embedded when
```

Re-embed on schedule (weekly) or on demand.

## Forbidden Patterns

- Sending raw production text to any LLM.
- Caching responses in plaintext outside the audit table.
- Fine-tuning a model on raw production data (the masked data path is the only path).
- Letting the LLM call functions / tools that perform state changes.
- "Agentic" loops where the LLM controls flow.

## What an Agent Should Do When Asked About AI Now

Politely surface that AI is Phase 7, list the pre-conditions, and offer to instead work on the prerequisites (data quality, masking, audit completeness). Do not begin AI implementation prematurely.

## Reference

Read `docs/10-ai-rag-strategy.md` for the full Phase 7 plan.
