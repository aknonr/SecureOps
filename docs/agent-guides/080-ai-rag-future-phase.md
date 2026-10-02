# 080 — Product AI / RAG (Phase 7)

This is about AI *inside the product*, not the coding agents working on the repository. Plan: `docs/10-ai-rag-strategy.md`, `plans/PHASE-7-private-ai-rag-poc.md`, ADR-0005.

## Preconditions — all required before any implementation

Phases 1–6 stable in production; at least 6 months of structured audit and diagnostic data; Bilgi Güvenliği and Siber Güvenlik approval of the data flow; approved separate budget; provisioned self-hosted GPU server; masking implemented and tested; ADR-0005 updated with the concrete rollout plan. If any is missing, say so and offer prerequisite work (data quality, masking, audit completeness) instead.

## Boundaries

- **Self-hosted only** (local model runtime, self-hosted vector store). No cloud inference APIs; vendor security copilots only with an explicit ADR and data-lineage analysis.
- **Suggests, never acts.** Summaries, pattern highlights, runbook pointers, shift-report drafts, questions over indexed audit data. It cannot trigger diagnostics, change tickets, notify, change state or bypass approvals; no tool calls with side effects and no agentic control loops.
- **Mask before inference** (hostnames, usernames, IPs, user paths, internal names) at the application layer; never fine-tune on unmasked production data.
- **Grounded and audited.** Answers come from retrieved records with references, or say there is not enough information. Every prompt (post-masking) and response is written to an append-only AI audit log with model name/version and token counts, same retention as operational audit.
- Model and vector-store choice is decided in the ADR-0005 update, against GPU budget, Turkish quality, licence and maintenance.
