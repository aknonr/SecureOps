# Phase 7 — Private AI / RAG PoC

**Duration:** 6–10 weeks (separate budget)
**Goal:** Pilot a self-hosted AI assistant for narrow operations use cases.

## Hard Pre-Conditions

**Phase 7 does not start until all of these are true:**

1. [ ] Phase 1–6 stable in production (90+ days).
2. [ ] At least 6 months of structured audit + diagnostic data accumulated.
3. [ ] Bilgi Güvenliği approval of data flow and masking strategy.
4. [ ] Siber Güvenlik approval of data flow and infrastructure boundary.
5. [ ] Budget approved for GPU server, storage, dedicated test environment.
6. [ ] Data masking strategy implemented and tested.
7. [ ] ADR-0005 amended with concrete model, hardware, and rollout decisions.

Missing any condition = Phase 7 blocked. Surface to the user.

## Deliverables

1. Self-hosted LLM environment (Ollama-based).
2. Self-hosted vector database (Qdrant or pgvector).
3. Data masking pipeline tested with sample data.
4. RAG retrieval and generation pipeline.
5. AI audit log (`audit.AiAuditLog`).
6. Limited Phase 7 UI: "AI Assistant" page in Blazor.
7. PoC for 3–4 use cases.
8. Operator feedback collected.

## Use Cases (Initial Scope)

1. Past-incident summarization.
2. Recurring-pattern highlighting.
3. Shift report drafting.
4. Runbook-aware suggestion.

Out of scope: agentic loops, tool use, autonomous diagnostics, decision making.

## Task Breakdown

### Sprint 1 — Infrastructure (~2 weeks)

| # | Task | Estimate |
|---|---|---|
| P7-T01 | GPU server procurement coordination | 4h |
| P7-T02 | Ollama installation and model load | 8h |
| P7-T03 | Qdrant installation and configuration | 6h |
| P7-T04 | Network isolation verification | 4h |
| P7-T05 | `audit.AiAuditLog` schema migration | 2h |

### Sprint 2 — Masking and Embedding (~2 weeks)

| # | Task | Estimate |
|---|---|---|
| P7-T06 | `IDataMasker` interface + implementation | 12h |
| P7-T07 | Masking test suite with diverse production-like data | 8h |
| P7-T08 | Embedding pipeline: pull from SQL → mask → embed → store | 12h |
| P7-T09 | Embedding schedule (weekly recurring) | 4h |
| P7-T10 | Embedding audit (what indexed when) | 3h |

### Sprint 3 — Retrieval and Generation (~2 weeks)

| # | Task | Estimate |
|---|---|---|
| P7-T11 | `IRetriever` implementation against Qdrant | 8h |
| P7-T12 | Prompt builder with grounding instruction | 6h |
| P7-T13 | `IOllamaClient` HTTP client | 6h |
| P7-T14 | Response validator (no hallucinated references) | 8h |
| P7-T15 | `IAiAuditWriter` for `audit.AiAuditLog` | 3h |
| P7-T16 | End-to-end pipeline test | 6h |

### Sprint 4 — Use Cases and UI (~2 weeks)

| # | Task | Estimate |
|---|---|---|
| P7-T17 | Use case: past-incident summary | 8h |
| P7-T18 | Use case: recurring pattern highlight | 8h |
| P7-T19 | Use case: shift report draft | 8h |
| P7-T20 | Use case: runbook-aware suggestion | 8h |
| P7-T21 | UI: AI Assistant page (Blazor) | 8h |
| P7-T22 | Audit display for AI interactions | 4h |

### Sprint 5 — Validation and Rollout (~1–2 weeks)

| # | Task | Estimate |
|---|---|---|
| P7-T23 | Operator feedback session | 4h |
| P7-T24 | Performance tuning | 6h |
| P7-T25 | Security re-review by Bilgi Güv + Siber Güv | 4h |
| P7-T26 | Documentation: AI feature guide | 4h |
| P7-T27 | Go / no-go for broader rollout | 2h |

**Total Phase 7 estimate:** ~170 hours, ~10 weeks at 18h/week. (Plus procurement lead time.)

## Hard Boundaries (From `.cursor/rules/080-ai-rag-future-phase-rules.mdc`)

- Self-hosted only. No OpenAI, Anthropic, Google, Cohere endpoints.
- Data masking before any LLM call.
- AI suggests; never acts.
- Full audit of every interaction.
- RAG grounding mandatory; no answers from training data alone.

## Exit Criteria

- [ ] All AI interactions audited.
- [ ] Zero unmasked data reaches the LLM (verified in masking tests).
- [ ] All 4 use cases produce useful outputs in operator review.
- [ ] Response time < 10 seconds for typical queries.
- [ ] Operators report the assistant is helpful (structured survey).
- [ ] DoD satisfied.

## Risks

| Risk | Mitigation |
|---|---|
| Hardware procurement delay | Start procurement early in Phase 6 if Phase 7 likely |
| Model Turkish quality poor | Test multiple candidates; pick best for the use case |
| Masking gap | Comprehensive test suite; canary scans |
| Hallucination | Response validator; clear "I don't know" response |
| Operator over-trust of AI | UI labels; training session emphasizes augment not replace |
| Phase 7 budget rejected | Project still valuable without AI; defer decision |

## Hand-off to Phase 8

Phase 8 plan: `plans/PHASE-8-approval-based-remediation.md`. Phase 8 is independent of Phase 7 — they can run on separate timelines.
